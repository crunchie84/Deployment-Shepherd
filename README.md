# Deployment Sheperd CLI

This tool is created to determine based on the given website url and branchname which subdir/slot it should be deployed to. It also communicates with Github to comment on pull requests to signal that they are being deployed or overwritten. It has mainly been developed for usage with Azure but can easily be adapted for different deployment solutions. It uses the following conventions:

- The website is deployed somewhere (mywebsite.azurewebsites.net)
- Individual deploy slots are named `pullrequestslot1` till `pullrequestslot4` and can be found by using the replacement char '{0}' in the given url
 (`mywebsite-{0}.azurewebsites.net`)
- It will poll all slots for activity by fetching `/api/status`:
 - 500 => slot is broken
 - 404 => slot is empty
 - 200 => parse details, fetch compile date + branch name from status json
- Based on the results per slot it will determine which slot to output as deployment environment to use

If all slots are filled it will try to find if a slot is filled with a closed pull request. If no slots contain closed pull requests it will take the oldest deployment. If that happens to be an open pull request it will comment on the pull request that it is no longer available at the deployment url.

If the branch for which we are searching is a pull request (`pull/123`) we will append a comment to the pull request signaling where the code is going to be deployed.


## Arguments

- `branchName` (refs/heads/feature/foo or refs/heads/pull/123 or refs/heads/master)
- `baseUrl` (mywebsite-{0}.azurewebsites.net)
- `Owner` (Crunchie84)
- `Repository` (my-awesome-repo)
- `PersonalGithubAccessToken` (asdf12sdf2r23rt23f23df23)

you can also use shorthands for the arguments, just run the program without arguments to see their exact shortcuts.

If you append `-d` to the arguments the application will also write debug information to the console about what it is going to do and NOT comment on github.

## Response

'pullrequest1' or '' if not needing to deploy to a subdomain slot.


## /api/status response json

We expect that the /api/status call will return json containing at least:

``
    {
      branchName: "refs/heads/develop",
      buildDate: "2014-10-15T09:09:38.152433Z"
    }
``

All other things are just to help yourself (version? git-commit-sha?). Please do note that for this tool to be effective the branchName returned should match with the branchname format passed to this tool (`refs/heads/develop` vs `develop` will not work properly).

# Repository

https://github.com/Q42/Deployment-Shepherd
