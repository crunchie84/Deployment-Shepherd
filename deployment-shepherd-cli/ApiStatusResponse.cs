using System;

namespace deployment_shepherd_cli
{
	/// <summary>
	/// The minimal expected api response fields
	/// </summary>
	public sealed class ApiStatusResponse
	{
		public string DeploySlotId { get; set; } 

		/// <summary>
		/// needed to be supplied by the deployed application so we can determine which branch it is
		/// </summary>
		public string BranchName { get; set; }

		/// <summary>
		/// Handy to determine which deploy is the oldest
		/// </summary>
		public DateTimeOffset BuildDate { get; set; }
		
		/// <summary>
		/// We append this ourselves in the code, should not be supplied by the deployed code
		/// </summary>
		public int? PullrequestId { get; set; }
	}
}