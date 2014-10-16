using PowerArgs;

namespace deployment_shepherd_cli
{
	public sealed class ProgramArguments
	{
		/// <summary>
		/// The baseurl to your deployment environment in which we will poll the pull request slots
		/// </summary>
		[ArgRequired]
		[ArgDescription("The baseurl + replacement char where we can find the slots")]
		[ArgExample("mywebsite-{0}.azurewebsites.net", "url")]
		[ArgShortcut("u")]
		public string BaseUrl { get; set; }

		/// <summary>
		/// The branchname we are currently wanting to deploy
		/// </summary>
		[ArgRequired]
		[ArgDescription("The branch which we are wanting to deploy")]
		[ArgExample("pull/123", "branch")]
		[ArgShortcut("b")]
		public string BranchName { get; set; }

		[ArgDescription("If true then we will emit information about what is done instead of only the branchName to use")]
		[ArgShortcut("d")]
		public bool DebugModeEnabled { get; set; }

		[ArgRequired]
		[ArgDescription("Owner of repository to which we are going to send the message")]
		[ArgExample("Crunchie84", "o")]
		[ArgShortcut("o")]
		public string Owner { get; set; }

		/// <summary>
		/// :owner/:repo thus Crunchie84/PullRequestCommenter
		/// </summary>
		[ArgRequired]
		[ArgDescription("Repository to which we are going to send messages")]
		[ArgExample("PullRequestCommenter", "r")]
		[ArgShortcut("r")]
		public string Repository { get; set; }

		[ArgRequired]
		[ArgDescription("The github personal access token, retrieved @https://help.github.com/articles/creating-an-access-token-for-command-line-use")]
		[ArgExample("34asdf4a5sdf34a5sdf6asdf45asdf5asdfsafd", "t")]
		[ArgShortcut("t")]
		public string PersonalGithubAccessToken { get; set; }
	}
}