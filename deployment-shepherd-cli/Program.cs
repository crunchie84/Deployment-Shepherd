using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Octokit;
using PowerArgs;

namespace deployment_shepherd_cli
{
	/// <summary>
	/// This application will go and find out which deployment slot to use. 
	/// It will try to poll github for closed pull requests (if the branchname is a pull request) so we do not close a living pull request
	/// 
	/// if the branchname is a pull request the application will also comment on the pull request with the deployment url to which it will be deployed
	/// </summary>
	class Program
	{
		static void Main(string[] args)
		{
			try
			{
				var parsedArgs = Args.Parse<ProgramArguments>(args);
				var task = FindDeploymentSlot(parsedArgs);
				task.Wait();
				Console.WriteLine(task.Result);

				Environment.Exit(0);
			}
			catch (ArgException ex)
			{
				Console.WriteLine(ex.Message);
				ArgUsage.GetStyledUsage<ProgramArguments>().Write();
				Environment.Exit(-2);

			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.Message);
				Environment.Exit(-1);
			}
		}

		private static async Task<string> FindDeploymentSlot(ProgramArguments args)
		{
			var slotItems = new List<Tuple<string, ApiStatusResponse>>();
			for (var i = 1; i <= 4; i++)
			{
				var slotId = "pullrequestslot" + i;
				var url = String.Format("http://{0}/api/status", String.Format(args.BaseUrl, slotId));
				using (var httpClient = new HttpClient())
				{
					consoleWriteLineIfDebug(args, "going to fetch: " + url);

					var result = await httpClient.GetAsync(url);
					consoleWriteLineIfDebug(args, "response status code = " + result.StatusCode);

					ApiStatusResponse apiResponse = null;
					if (result.IsSuccessStatusCode)
					{
						try
						{
							apiResponse = JsonConvert.DeserializeObject<ApiStatusResponse>(await result.Content.ReadAsStringAsync());
							apiResponse.PullrequestId = parsePullRequestIdFromBranchName(apiResponse.BranchName);
						}
						catch (Exception ex)
						{
							consoleWriteLineIfDebug(args, "Failed to parse api/status response: " + ex.Message);
						}
					}
					slotItems.Add(Tuple.Create(slotId, apiResponse));
				}
			}

			var currentSlot =
				slotItems.Where(sl => sl.Item2 != null && sl.Item2.BranchName == args.BranchName)
				.Select(sl => sl.Item1)
				.FirstOrDefault();
			if (!String.IsNullOrEmpty(currentSlot))
			{
				// we are already deployed here, no further comments are required (if we are a pull request)
				consoleWriteLineIfDebug(args, "We found a slot in which we are already deployed = " + currentSlot);
				return currentSlot;
			}

			var pullRequestId = parsePullRequestIdFromBranchName(args.BranchName);
			var githubClient = new GitHubClient(new ProductHeaderValue("Q42.PullRequestCommenter"))
			{
				Credentials = new Credentials(args.PersonalGithubAccessToken)
			};

			var anEmptySlot = slotItems.Where(sl => sl.Item2 == null)
				.Select(sl => sl.Item1)
				.FirstOrDefault();
			if (!String.IsNullOrEmpty(anEmptySlot))
			{
				consoleWriteLineIfDebug(args, "We found an empty slot in which we can deploy = " + anEmptySlot);

				if (pullRequestId != null)
					await addPullRquestCommentAboutDeploymentSlot(githubClient, args, pullRequestId.Value, anEmptySlot);
				return anEmptySlot;
			}

			// find a slot with a closed pull request in it
			string closedPullRequestSlot = null;
			var slotsContainingPullRequests = slotItems.Where(sl => sl.Item2 != null && sl.Item2.PullrequestId != null);
			foreach (var slotContainingPullRequest in slotsContainingPullRequests)
			{
				var isClosed = await isClosedPullRequest(githubClient, args, slotContainingPullRequest.Item2.PullrequestId.Value);
				if (isClosed)
				{
					closedPullRequestSlot = slotContainingPullRequest.Item1;
					break;
				}
			}
			if (!String.IsNullOrWhiteSpace(closedPullRequestSlot))
			{
				consoleWriteLineIfDebug(args, String.Format("Found a slot ({0}) containing a closed pullrequest which we are going to use", closedPullRequestSlot));

				if (pullRequestId != null)
					await addPullRquestCommentAboutDeploymentSlot(githubClient, args, pullRequestId.Value, closedPullRequestSlot);
				return closedPullRequestSlot;
			}

			// last resort - use the oldest deploy
			var theOldestSlot = slotItems.Where(sl => sl.Item2 != null)
				.OrderBy(sl => sl.Item2.BuildDate)
				.FirstOrDefault();
			if (theOldestSlot != null)
			{
				consoleWriteLineIfDebug(args, "We found the oldest slot in which we can deploy = " + theOldestSlot.Item1);
				if (theOldestSlot.Item2.PullrequestId != null)
				{
					consoleWriteLineIfDebug(args, "Found the oldest slot but it still contains a open pull request. Going to comment it.");
					await addCommentToPullRequest(githubClient, args, theOldestSlot.Item2.PullrequestId.Value,
						":warning::no_entry: This deployment has been overwritten by another pull request thus is no longer available :no_entry::warning:");
				}

				if (pullRequestId != null)
					await addPullRquestCommentAboutDeploymentSlot(githubClient, args, pullRequestId.Value, theOldestSlot.Item1);
				return theOldestSlot.Item1;
			}

			throw new Exception("This should not happen, we should always select some slot");
		}

		private static async Task addPullRquestCommentAboutDeploymentSlot(GitHubClient githubClient, ProgramArguments args, int pullRequestId, string deploymentSlot)
		{
			await addCommentToPullRequest(githubClient, args, pullRequestId,
				String.Format(CultureInfo.InvariantCulture, "Going to deploy this pull request to http://{0} so you can easily test it.",
					String.Format(CultureInfo.InvariantCulture, args.BaseUrl, deploymentSlot)));
		}

		private static void consoleWriteLineIfDebug(ProgramArguments args, string message)
		{
			if (args.DebugModeEnabled)
				Console.WriteLine("[DEBUG] " + message);
		}

		private static async Task<bool> isClosedPullRequest(GitHubClient githubClient, ProgramArguments args,
			int pullRequestId)
		{
			var pr = await githubClient.PullRequest.Get(args.Owner, args.Repository, pullRequestId);
			return pr.State == ItemState.Closed;
		}

		private static async Task addCommentToPullRequest(GitHubClient githubClient, ProgramArguments args, int pullRequestId, string message)
		{
			if (args.DebugModeEnabled)
				Console.WriteLine("[DEBUG] Was going to append comment to pullrequest {0} => '{1}'", pullRequestId, message);
			else
				await githubClient.Issue.Comment.Create(args.Owner, args.Repository, pullRequestId, message);
		}

		/// <summary>
		/// try to locate the pullrequest id in the given branchName
		/// </summary>
		/// <param name="branchName"></param>
		/// <returns></returns>
		private static int? parsePullRequestIdFromBranchName(string branchName)
		{
			if (branchName.Contains("pull"))
			{
				int pullRequestId;
				if (Int32.TryParse(Regex.Replace(branchName, "[^0-9]", ""), out pullRequestId))
				{
					//we have a pull request id, 
					return pullRequestId;
				}
			}
			return null;
		}
	}
}
