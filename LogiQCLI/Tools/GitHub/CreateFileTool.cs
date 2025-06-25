using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Octokit;

using LogiQCLI.Infrastructure.ApiClients.GitHub;
using LogiQCLI.Tools.GitHub.Objects;
using LogiQCLI.Tools.Core.Objects;
using LogiQCLI.Tools.Core.Interfaces;
using LogiQCLI.Infrastructure.ApiClients.GitHub.Objects;

namespace LogiQCLI.Tools.GitHub
{
    [ToolMetadata("GitHub", Tags = new[] { "github", "create" })]
    public class CreateGitHubFileTool : ITool
    {
        private readonly GitHubClientWrapper _gitHubClient;

        public override List<string> RequiredServices => new List<string> { "GitHubClientWrapper" };

        public CreateGitHubFileTool(GitHubClientWrapper gitHubClient)
        {
            _gitHubClient = gitHubClient;
        }

        public override RegisteredTool GetToolInfo()
        {
            return new RegisteredTool
            {
                Name = "create_github_file",
                Description = "Create new files in GitHub repositories with commit message. Requires GitHub authentication token with write access.",
                Parameters = new Parameters
                {
                    Type = "object",
                    Properties = new
                    {
                        owner = new
                        {
                            type = "string",
                            description = "Repository owner (username or organization). Required unless default configured."
                        },
                        repo = new
                        {
                            type = "string",
                            description = "Repository name. Required unless default configured."
                        },
                        path = new
                        {
                            type = "string",
                            description = "File path within repository where file will be created. Example: 'src/main.js', 'docs/README.md'"
                        },
                        content = new
                        {
                            type = "string",
                            description = "File content to create. Supports any text content including code, documentation, configuration."
                        },
                        message = new
                        {
                            type = "string",
                            description = "Commit message describing file creation. Example: 'Add new configuration file'"
                        },
                        branch = new
                        {
                            type = "string",
                            description = "Branch name to create file on. Default: repository's default branch."
                        },
                        authorName = new
                        {
                            type = "string",
                            description = "Author name for commit. If not provided, uses authenticated user's name."
                        },
                        authorEmail = new
                        {
                            type = "string",
                            description = "Author email for commit. If not provided, uses authenticated user's email."
                        }
                    },
                    Required = new[] { "path", "content", "message" }
                }
            };
        }

        public override async Task<string> Execute(string args)
        {
            try
            {
                var arguments = JsonSerializer.Deserialize<CreateFileArguments>(args);
                if (arguments == null || string.IsNullOrEmpty(arguments.Path) || 
                    string.IsNullOrEmpty(arguments.Content) || string.IsNullOrEmpty(arguments.Message))
                {
                    return "Error: Invalid arguments. Path, content, and commit message are required.";
                }

                if (string.IsNullOrEmpty(arguments.Owner) || string.IsNullOrEmpty(arguments.Repo))
                {
                    return "Error: Owner and repo are required. Configure default values or provide them explicitly.";
                }

                var createRequest = new CreateFileRequest(arguments.Message, arguments.Content, arguments.Branch ?? "main");
                
                if (!string.IsNullOrEmpty(arguments.AuthorName) && !string.IsNullOrEmpty(arguments.AuthorEmail))
                {
                    createRequest.Author = new Committer(arguments.AuthorName, arguments.AuthorEmail, DateTimeOffset.UtcNow);
                }

                var result = await _gitHubClient.CreateFileAsync(arguments.Owner, arguments.Repo, arguments.Path, createRequest);

                var response = $"Successfully created file {arguments.Path} in {arguments.Owner}/{arguments.Repo}\n";
                response += $"Commit SHA: {result.Commit.Sha}\n";
                response += $"Commit Message: {arguments.Message}\n";
                response += $"Branch: {arguments.Branch ?? "main"}\n";
                response += $"File URL: {result.Content.HtmlUrl}";

                return response;
            }
            catch (GitHubClientException ex)
            {
                return $"GitHub API Error: {ex.Message}";
            }
            catch (Exception ex)
            {
                return $"Error creating GitHub file: {ex.Message}";
            }
        }
    }
}
