# Deployment Sheperd CLI

This tool is created to determine based on the given website url and branchname which subdir/slot it should be deployed to. It has mainly been developed for usage with Azure but can easily be adapted for different deployment solutions. It uses the following conventions:

- The website is deployed somewhere (mywebsite.azurewebsites.net)
- Individual deploy slots are named `pullrequest1` till `pullrequest4` and can be found by using the replacement char '{0}' in the given url
 (`mywebsite-{0}.azurewebsites.net`)
- It will poll all slots for activity by fetching `/api/status`:
 - 500 => slot is broken
 - 404 => slot is empty
 - 200 => parse details, fetch compile date + branch name from status json
- Based on the results per slot it will determine which slot to output as deployment environment to use

## Arguments

- `branchName` (refs/heads/feature/foo or refs/heads/pull/123 or refs/heads/master)
- `baseUrl` (mywebsite-{0}.azurewebsites.net)

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

All other things are just to help yourself (version? git-commit-sha?)

# Repository

https://github.com/Q42/Deployment-Shepherd