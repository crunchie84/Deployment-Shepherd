using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
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
				var task = findDeploymentSlot(parsedArgs);
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

		private static async Task<Tuple<string, ApiStatusResponse, bool>> getStatusObjectWithGithubData(ProgramArguments args, string slotId, GitHubClient githubClient)
		{
			var url = String.Format("http://{0}/api/status", String.Format(args.BaseUrl, slotId));
			var wc = new WebClient();
			var apiStatusString = await wc.DownloadStringTaskAsync(url);
			consoleWriteLineIfDebug(args, "Got response of " + url + " : " + apiStatusString);
			var apiStatus = JsonConvert.DeserializeObject<ApiStatusResponse>(apiStatusString);

			/* determine if what lives in this slot is a pull request */
			apiStatus.PullrequestId = parsePullRequestIdFromBranchName(apiStatus.BranchName);
			apiStatus.DeploySlotId = slotId;
			return Tuple.Create(slotId, apiStatus, await isClosedPullRequest(githubClient, args, apiStatus.PullrequestId));
		}

		private static IObservable<Tuple<string, ApiStatusResponse, bool>> retrieveSlotStatusWithFallBack(ProgramArguments args, string slotId, GitHubClient githubClient)
		{
			return Observable
				.Defer(() => getStatusObjectWithGithubData(args, slotId, githubClient).ToObservable())
				.Retry(3) // retry 3 times to poll the slot or use the fallback
				.Catch<Tuple<string, ApiStatusResponse, bool>, Exception>(
					ex => Observable.Return(Tuple.Create(slotId, (ApiStatusResponse)null, true)));
		}

		private static async Task<string> findDeploymentSlot(ProgramArguments args)
		{
			var githubClient = new GitHubClient(new ProductHeaderValue("Q42.PullRequestCommenter"))
			{
				Credentials = new Credentials(args.PersonalGithubAccessToken)
			};
			var pullRequestId = parsePullRequestIdFromBranchName(args.BranchName);

			var slotItems = await Observable.Range(1, 4)
				.Select(idx => "pullrequestslot" + idx)
				.SelectMany(slotId => retrieveSlotStatusWithFallBack(args, slotId, githubClient))
				.ToList();

			var currentSlot = slotItems.Where(sl => sl.Item2 != null && sl.Item2.BranchName == args.BranchName).Select(sl => sl.Item1).FirstOrDefault();
			if (!String.IsNullOrEmpty(currentSlot))
			{
				// we are already deployed here, no further comments are required (if we are a pull request)
				consoleWriteLineIfDebug(args, "We found a slot in which we are already deployed = " + currentSlot);
				return currentSlot;
			}

			var anEmptySlot = slotItems.Where(sl => sl.Item2 == null).Select(sl => sl.Item1).FirstOrDefault();
			if (!String.IsNullOrEmpty(anEmptySlot))
			{
				consoleWriteLineIfDebug(args, "We found an empty slot in which we can deploy = " + anEmptySlot);

				if (pullRequestId != null)
					await addPullRquestCommentAboutDeploymentSlot(githubClient, args, pullRequestId.Value, anEmptySlot);

				return anEmptySlot;
			}

			// find a slot with a closed pull request in it
			var aClosedPullRequestSlot = slotItems.Where(sl => sl.Item2 != null && sl.Item2.PullrequestId != null && sl.Item3 == true).Select(sl => sl.Item1).FirstOrDefault();
			if (!String.IsNullOrEmpty(aClosedPullRequestSlot))
			{
				consoleWriteLineIfDebug(args, String.Format("Found a slot ({0}) containing a closed pullrequest which we are going to use", aClosedPullRequestSlot));

				if (pullRequestId != null)
					await addPullRquestCommentAboutDeploymentSlot(githubClient, args, pullRequestId.Value, aClosedPullRequestSlot);

				return aClosedPullRequestSlot;
			}

			// last resort - use the oldest deploy
			var theOldestSlot = slotItems.Where(sl => sl.Item2 != null).OrderBy(sl => sl.Item2.BuildDate).FirstOrDefault();
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

		private static async Task<bool> isClosedPullRequest(GitHubClient githubClient, ProgramArguments args, int? pullRequestId)
		{
			if (pullRequestId == null)
				return true;

			var pr = await githubClient.PullRequest.Get(args.Owner, args.Repository, pullRequestId.Value);
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
