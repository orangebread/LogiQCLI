using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LogiQCLI.Tools.Core.Objects;
using LogiQCLI.Tools.ContentManipulation.Objects;
using LogiQCLI.Tools.ContentManipulation.Arguments;
using LogiQCLI.Tools.Core.Interfaces;

namespace LogiQCLI.Tools.ContentManipulation
{
    [ToolMetadata("ContentManipulation", Tags = new[] { "essential", "safe", "query" })]
    public class SearchFilesTool : ITool
    {
        public override RegisteredTool GetToolInfo()
        {
            return new RegisteredTool
            {
                Name = "search_files",
                Description = "Search for text patterns across multiple files with regex support. Returns matching lines with file locations and line numbers.",
                Parameters = new Parameters
                {
                    Type = "object",
                    Properties = new
                    {
                        pattern = new
                        {
                            type = "string",
                            description = "Search pattern (plain text or regex). Examples: 'TODO:', 'class\\s+\\w+Manager'"
                        },
                        path = new
                        {
                            type = "string",
                            description = "Directory to search relative to workspace. Default: '.' (workspace root)."
                        },
                        file_pattern = new
                        {
                            type = "string",
                            description = "File name pattern filter (wildcards supported). Default: '*'. Examples: '*.cs', 'Test*.cs'"
                        },
                        use_regex = new
                        {
                            type = "boolean",
                            description = "Treat pattern as regex. Default: false."
                        },
                        case_sensitive = new
                        {
                            type = "boolean",
                            description = "Case-sensitive search. Default: true."
                        },
                        max_results = new
                        {
                            type = "integer",
                            description = "Maximum results to return. Default: 50. Use -1 for unlimited."
                        }
                    },
                    Required = new[] { "pattern" }
                }
            };
        }

        public override async Task<string> Execute(string args)
        {
            try
            {
                var arguments = JsonSerializer.Deserialize<SearchFilesArguments>(args);
                if (arguments == null || string.IsNullOrEmpty(arguments.Pattern))
                {
                    return "Error: Invalid arguments. Pattern is required.";
                }

                var searchPath = string.IsNullOrEmpty(arguments.Path) ? "." : arguments.Path;
                var cleanPath = searchPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                var fullPath = Path.GetFullPath(cleanPath);

                if (!Directory.Exists(fullPath))
                {
                    return $"Error: Directory does not exist: {fullPath}";
                }

                var filePattern = string.IsNullOrEmpty(arguments.FilePattern) ? "*" : arguments.FilePattern;
                var useRegex = arguments.UseRegex ?? false;
                var caseSensitive = arguments.CaseSensitive ?? true;
                var maxResults = arguments.MaxResults ?? 50;

                // Validate regex pattern if using regex
                if (useRegex)
                {
                    try
                    {
                        new Regex(arguments.Pattern);
                    }
                    catch (ArgumentException ex)
                    {
                        return $"Error: Invalid regex pattern '{arguments.Pattern}': {ex.Message}";
                    }
                }

                var results = new List<string>();
                var files = Directory.GetFiles(fullPath, filePattern, SearchOption.AllDirectories);
                var workspaceRoot = Directory.GetCurrentDirectory();
                var resultCount = 0;

                foreach (var file in files)
                {
                    if (resultCount >= maxResults && maxResults != -1)
                        break;

                    try
                    {
                        var content = await File.ReadAllTextAsync(file);
                        var lines = content.Split('\n');
                        var relativePath = Path.GetRelativePath(workspaceRoot, file);

                        for (int i = 0; i < lines.Length; i++)
                        {
                            if (resultCount >= maxResults && maxResults != -1)
                                break;

                            bool isMatch = false;
                            
                            if (useRegex)
                            {
                                var options = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                                isMatch = Regex.IsMatch(lines[i], arguments.Pattern, options);
                            }
                            else
                            {
                                var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                                isMatch = lines[i].Contains(arguments.Pattern, comparison);
                            }

                            if (isMatch)
                            {
                                results.Add($"{relativePath}:{i + 1}: {lines[i].Trim()}");
                                resultCount++;
                            }
                        }
                    }
                    catch
                    {

                    }
                }

                if (results.Count == 0)
                {
                    return $"No matches found for pattern '{arguments.Pattern}' in {fullPath}";
                }

                var output = new StringBuilder();
                output.AppendLine($"Found {results.Count} matches for '{arguments.Pattern}':");
                output.AppendLine();
                
                foreach (var result in results)
                {
                    output.AppendLine(result);
                }

                if (resultCount >= maxResults && maxResults != -1)
                {
                    output.AppendLine();
                    output.AppendLine($"(Limited to first {maxResults} results)");
                }

                return output.ToString();
            }
            catch (Exception ex)
            {
                return $"Error searching files: {ex.Message}";
            }
        }

    }
}
