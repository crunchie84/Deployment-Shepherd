using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using PowerArgs;

namespace deployment_shepherd_cli
{
	public sealed class ProgramArguments
	{
		/// <summary>
		/// The baseurl to your deployment environment in which we will poll the pull request slots
		/// </summary>
		[ArgRequired]
		[ArgPosition(0)]
		[ArgDescription("The baseurl + replacement char where we can find the slots")]
		[ArgExample("mywebsite-{0}.azurewebsites.net", "url")]
		[ArgShortcut("u")]
		public string BaseUrl { get; set; }

		/// <summary>
		/// The branchname we are currently wanting to deploy
		/// </summary>
		[ArgRequired]
		[ArgPosition(1)]
		[ArgDescription("The branch which we are wanting to deploy")]
		[ArgExample("/head/pull/123", "branch")]
		[ArgShortcut("b")]
		public string BranchName { get; set; }

		[ArgPosition(2)]
		[ArgDescription("If true then we will emit information about what is done instead of only the branchName to use")]
		[ArgShortcut("v")]
		public bool ShowDebugMessage { get; set; }
	}

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
					if (args.ShowDebugMessage)
						Console.WriteLine("[DEBUG] going to fetch: " + url);

					var result = await httpClient.GetAsync(url);

					if (args.ShowDebugMessage)
						Console.WriteLine("[DEBUG] response status code = " + result.StatusCode);

					ApiStatusResponse apiResponse = null;
					if (result.IsSuccessStatusCode)
					{
						try
						{
							apiResponse = JsonConvert.DeserializeObject<ApiStatusResponse>(await result.Content.ReadAsStringAsync());
						}
						catch (Exception ex)
						{
							if (args.ShowDebugMessage)
								Console.WriteLine("Failed to parse api/status response: " + ex.Message);
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
				if (args.ShowDebugMessage)
					Console.WriteLine("[DEBUG] We found a slot in which we are already deployed = " + currentSlot);
				return currentSlot;
			}

			var anEmptySlot = slotItems.Where(sl => sl.Item2 == null)
				.Select(sl => sl.Item1)
				.FirstOrDefault();
			if (!String.IsNullOrEmpty(anEmptySlot))
			{
				if (args.ShowDebugMessage)
					Console.WriteLine("[DEBUG] We found an empty slot in which we can deploy = " + anEmptySlot);
				return anEmptySlot;
			}
			
			var theOldestSlot = slotItems.Where(sl => sl.Item2 != null)
				.OrderBy(sl => sl.Item2.BuildDate)
				.Select(sl => sl.Item1)
				.FirstOrDefault();
			if (!String.IsNullOrEmpty(theOldestSlot))
			{
				if (args.ShowDebugMessage)
					Console.WriteLine("[DEBUG] We found the oldest slot in which we can deploy = " + theOldestSlot);
				return theOldestSlot;
			}

			throw new Exception("This should not happen, we should always select some slot");
		}
	}

	/// <summary>
	/// The minimal expected api response fields
	/// </summary>
	public sealed class ApiStatusResponse
	{
		public string BranchName { get; set; }
		public DateTimeOffset BuildDate { get; set; }
	}
}
