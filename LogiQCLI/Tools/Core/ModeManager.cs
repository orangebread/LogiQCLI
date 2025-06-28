using System;
using System.Collections.Generic;
using System.Linq;
using LogiQCLI.Core.Models.Configuration;
using LogiQCLI.Core.Models.Modes;
using LogiQCLI.Core.Models.Modes.Interfaces;
using LogiQCLI.Tools.Core.Interfaces;

namespace LogiQCLI.Core.Services
{
    public class ModeManager : IModeManager
    {
        private readonly ConfigurationService _configurationService;
        private readonly IToolRegistry _toolRegistry;
        private ModeSettings _modeSettings;
        private Mode _currentMode;

        public ModeManager(ConfigurationService configurationService, IToolRegistry toolRegistry)
        {
            _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
            _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
            InitializeModeSettings();
            
            var mode = GetMode(_modeSettings?.ActiveModeId ?? "default");
            if (mode == null)
            {
                mode = GetMode("default");
                if (mode == null)
                    throw new InvalidOperationException("Default mode not found. System is in an invalid state.");
                _modeSettings.ActiveModeId = "default";
                SaveModeSettings();
            }
            
            _currentMode = mode;
        }

        public Mode GetCurrentMode()
        {
            _currentMode.AllowedTools = ResolveToolsForMode(_currentMode);
            return _currentMode;
        }

        public bool SetCurrentMode(string modeId)
        {
            if (string.IsNullOrWhiteSpace(modeId))
                throw new ArgumentNullException(nameof(modeId), "Mode ID cannot be null or empty.");

            var mode = GetMode(modeId);
            if (mode == null)
                throw new InvalidOperationException($"Mode with ID '{modeId}' does not exist.");
            
            mode.AllowedTools = ResolveToolsForMode(mode);

            _currentMode = mode;
            _modeSettings.ActiveModeId = modeId;
            SaveModeSettings();
            return true;
        }

        public List<Mode> GetAvailableModes()
        {
            var allModes = new List<Mode>();
            allModes.AddRange(_modeSettings.DefaultModes);
            allModes.AddRange(_modeSettings.CustomModes);
            
            foreach (var mode in allModes)
            {
                mode.AllowedTools = ResolveToolsForMode(mode);
            }
            return allModes;
        }

        public Mode? GetMode(string modeId)
        {
            var mode = GetAvailableModes().FirstOrDefault(m => m.Id == modeId);
            if (mode != null)
            {
                mode.AllowedTools = ResolveToolsForMode(mode);
            }
            return mode;
        }

        public bool AddCustomMode(Mode mode)
        {
            if (mode == null)
                throw new ArgumentNullException(nameof(mode), "Mode cannot be null.");

            if (string.IsNullOrWhiteSpace(mode.Id))
                throw new ArgumentException("Mode ID cannot be null or empty.", nameof(mode));

            if (GetMode(mode.Id) != null)
                throw new InvalidOperationException($"Mode with ID '{mode.Id}' already exists.");

            mode.AllowedTools = ResolveToolsForMode(mode);
            if (mode.AllowedTools == null || !mode.AllowedTools.Any())
                throw new ArgumentException("Mode must have at least one allowed tool after resolving categories and tags.", nameof(mode));

            mode.IsBuiltIn = false;
            _modeSettings.CustomModes.Add(mode);
            SaveModeSettings();
            return true;
        }

        public bool RemoveCustomMode(string modeId)
        {
            if (string.IsNullOrWhiteSpace(modeId))
                throw new ArgumentNullException(nameof(modeId), "Mode ID cannot be null or empty.");

            var mode = _modeSettings.CustomModes.FirstOrDefault(m => m.Id == modeId);
            if (mode == null)
                throw new InvalidOperationException($"Custom mode with ID '{modeId}' does not exist.");

            if (mode.IsBuiltIn)
                throw new InvalidOperationException("Cannot remove built-in modes.");

            _modeSettings.CustomModes.Remove(mode);
            
            if (_currentMode.Id == modeId)
            {
                var defaultMode = GetMode("default");
                if (defaultMode == null)
                    throw new InvalidOperationException("Default mode not found.");
                    
                _currentMode = defaultMode;
                _modeSettings.ActiveModeId = "default";
            }
            
            SaveModeSettings();
            return true;
        }

        public bool IsToolAllowedInCurrentMode(string toolName)
        {
            return _currentMode.AllowedTools.Contains(toolName);
        }

        public int GetBuiltInModeCount()
        {
            return _modeSettings.DefaultModes.Count(m => m.IsBuiltIn);
        }

        private void InitializeModeSettings()
        {
            var settings = _configurationService.LoadSettings();
            
            if (settings?.ModeSettings == null)
            {
                _modeSettings = new ModeSettings
                {
                    DefaultModes = BuiltInModes.GetBuiltInModes(),
                    CustomModes = new List<Mode>(),
                    ActiveModeId = "default"
                };
                SaveModeSettings();
                return;
            }

            _modeSettings = settings.ModeSettings;
            bool needsSave = false;


            if (_modeSettings.DefaultModes == null)
            {
                _modeSettings.DefaultModes = new List<Mode>();
                needsSave = true;
            }


            if (_modeSettings.CustomModes == null)
            {
                _modeSettings.CustomModes = new List<Mode>();
                needsSave = true;
            }


            needsSave = MigrateBuiltInModes() || needsSave;


            var validCustomModes = _modeSettings.CustomModes
                .Where(m => IsValidMode(m))
                .ToList();

            if (validCustomModes.Count != _modeSettings.CustomModes.Count)
            {
                _modeSettings.CustomModes = validCustomModes;
                needsSave = true;
            }
            
            if (string.IsNullOrWhiteSpace(_modeSettings.ActiveModeId) || 
                !GetAvailableModes().Any(m => m.Id == _modeSettings.ActiveModeId))
            {
                _modeSettings.ActiveModeId = "default";
                needsSave = true;
            }

            if (needsSave)
            {
                SaveModeSettings();
            }
        }

        private bool MigrateBuiltInModes()
        {
            var currentBuiltInModes = BuiltInModes.GetBuiltInModes();
            var existingModeIds = _modeSettings.DefaultModes.Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            bool hasChanges = false;
            var addedModes = new List<string>();
            var updatedModes = new List<string>();
            var removedModes = new List<string>();


            foreach (var newMode in currentBuiltInModes)
            {
                var existingMode = _modeSettings.DefaultModes.FirstOrDefault(m => 
                    string.Equals(m.Id, newMode.Id, StringComparison.OrdinalIgnoreCase));

                if (existingMode == null)
                {

                    _modeSettings.DefaultModes.Add(newMode);
                    addedModes.Add(newMode.Id);
                    hasChanges = true;
                }
                else
                {

                    if (ShouldUpdateBuiltInMode(existingMode, newMode))
                    {

                        UpdateBuiltInMode(existingMode, newMode);
                        updatedModes.Add(existingMode.Id);
                        hasChanges = true;
                    }
                }
            }


            var currentModeIds = currentBuiltInModes.Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var modesToRemove = _modeSettings.DefaultModes
                .Where(m => m.IsBuiltIn && !currentModeIds.Contains(m.Id))
                .ToList();

            foreach (var modeToRemove in modesToRemove)
            {
                _modeSettings.DefaultModes.Remove(modeToRemove);
                removedModes.Add(modeToRemove.Id);
                hasChanges = true;
            }
            
            if (hasChanges)
            {
                LogMigrationInfo(addedModes, updatedModes, removedModes);
            }

            return hasChanges;
        }

        private void LogMigrationInfo(List<string> addedModes, List<string> updatedModes, List<string> removedModes)
        {
            if (addedModes.Any())
            {
                Console.WriteLine($"[LogiQCLI] Added new built-in modes: {string.Join(", ", addedModes)}");
            }
            
            if (updatedModes.Any())
            {
                Console.WriteLine($"[LogiQCLI] Updated built-in modes: {string.Join(", ", updatedModes)}");
            }
            
            if (removedModes.Any())
            {
                Console.WriteLine($"[LogiQCLI] Removed obsolete built-in modes: {string.Join(", ", removedModes)}");
            }
        }

        private bool ShouldUpdateBuiltInMode(Mode existingMode, Mode newMode)
        {

            if (!existingMode.IsBuiltIn)
                return false;


            return existingMode.Name != newMode.Name ||
                   existingMode.Description != newMode.Description ||
                   existingMode.SystemPrompt != newMode.SystemPrompt ||
                   existingMode.PreferredModel != newMode.PreferredModel ||
                   !ListsEqual(existingMode.AllowedCategories, newMode.AllowedCategories) ||
                   !ListsEqual(existingMode.ExcludedCategories, newMode.ExcludedCategories) ||
                   !ListsEqual(existingMode.AllowedTags, newMode.AllowedTags) ||
                   !ListsEqual(existingMode.ExcludedTags, newMode.ExcludedTags) ||
                   !ListsEqual(existingMode.AllowedTools, newMode.AllowedTools);
        }

        private void UpdateBuiltInMode(Mode existingMode, Mode newMode)
        {
            existingMode.Name = newMode.Name;
            existingMode.Description = newMode.Description;
            existingMode.SystemPrompt = newMode.SystemPrompt;
            existingMode.PreferredModel = newMode.PreferredModel;
            existingMode.IsBuiltIn = newMode.IsBuiltIn;
            
            existingMode.AllowedCategories = new List<string>(newMode.AllowedCategories);
            existingMode.ExcludedCategories = new List<string>(newMode.ExcludedCategories);
            existingMode.AllowedTags = new List<string>(newMode.AllowedTags);
            existingMode.ExcludedTags = new List<string>(newMode.ExcludedTags);
            existingMode.AllowedTools = new List<string>(newMode.AllowedTools);
        }

        private bool ListsEqual<T>(List<T> list1, List<T> list2)
        {
            if (list1 == null && list2 == null) return true;
            if (list1 == null || list2 == null) return false;
            if (list1.Count != list2.Count) return false;
            
            var set1 = new HashSet<T>(list1, EqualityComparer<T>.Default);
            var set2 = new HashSet<T>(list2, EqualityComparer<T>.Default);
            
            return set1.SetEquals(set2);
        }

        private bool IsValidMode(Mode mode)
        {
            if (mode == null) return false;
            if (string.IsNullOrWhiteSpace(mode.Id)) return false;
            if (mode.AllowedTools == null || !mode.AllowedTools.Any()) return false;
            
            var distinctTools = mode.AllowedTools.Distinct().ToList();
            if (distinctTools.Count != mode.AllowedTools.Count)
            {
                mode.AllowedTools = distinctTools;
            }

            return true;
        }

        private void SaveModeSettings()
        {
            var settings = _configurationService.LoadSettings() ?? new ApplicationSettings();
            settings.ModeSettings = _modeSettings;
            _configurationService.SaveSettings(settings);
        }

        private List<string> ResolveToolsForMode(Mode mode)
        {
            if (mode == null) return new List<string>();

            var resolvedTools = new HashSet<string>(mode.AllowedTools, StringComparer.OrdinalIgnoreCase);

            if (mode.AllowedCategories.Any())
            {
                foreach (var category in mode.AllowedCategories)
                {
                    var toolsInCategory = _toolRegistry.GetToolsByCategory(category);
                    foreach (var tool in toolsInCategory)
                    {
                        resolvedTools.Add(tool.Name);
                    }
                }
            }

            if (mode.AllowedTags.Any())
            {
                foreach (var tag in mode.AllowedTags)
                {
                    var toolsWithTag = _toolRegistry.GetToolsByTag(tag);
                    foreach (var tool in toolsWithTag)
                    {
                        resolvedTools.Add(tool.Name);
                    }
                }
            }
            
            if (mode.ExcludedCategories.Any())
            {
                foreach (var category in mode.ExcludedCategories)
                {
                    var toolsInCategory = _toolRegistry.GetToolsByCategory(category);
                    foreach (var tool in toolsInCategory)
                    {
                        resolvedTools.Remove(tool.Name);
                    }
                }
            }

            if (mode.ExcludedTags.Any())
            {
                foreach (var tag in mode.ExcludedTags)
                {
                    var toolsWithTag = _toolRegistry.GetToolsByTag(tag);
                    foreach (var tool in toolsWithTag)
                    {
                        resolvedTools.Remove(tool.Name);
                    }
                }
            }

            return resolvedTools.ToList();
        }
    }
}
