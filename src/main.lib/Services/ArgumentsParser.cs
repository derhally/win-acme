﻿using PKISharp.WACS.Configuration.Arguments;
using PKISharp.WACS.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PKISharp.WACS.Configuration
{
    public class ArgumentsParser
    {
        private readonly ILogService _log;
        private readonly string[] _args;
        private readonly IEnumerable<IArgumentsProvider> _providers;

        public T? GetArguments<T>() where T : class, new()
        {
            foreach (var provider in _providers)
            {
                if (provider is IArgumentsProvider<T> typedProvider)
                {
                    return typedProvider.GetResult(_args);
                }
            }
            throw new InvalidOperationException($"Unable to find class that implements IArgumentsProvider<{typeof(T).Name}>");
        }

        public ArgumentsParser(ILogService log, IPluginService plugins, string[] args)
        {
            _log = log;
            _args = args;
            _providers = plugins.ArgumentsProviders();
        }

        internal bool Validate()
        {
            // Test if the arguments can be resolved by any of the known providers
            var superset = _providers.SelectMany(x => x.Configuration);
            var result = _providers.First().GetParseResult(_args);
            foreach (var add in result.AdditionalOptionsFound)
            {
                var super = superset.FirstOrDefault(x => string.Equals(x.LongName, add.Key, StringComparison.InvariantCultureIgnoreCase));
                if (super == null)
                {
                    _log.Error("Unknown argument --{0}", add.Key);
                    return false;
                }
            }

            // Run indivual result validations
            var main = GetArguments<MainArguments>();
            if (main == null)
            {
                return false;
            }
            var mainProvider = _providers.OfType<IArgumentsProvider<MainArguments>>().First();
            if (mainProvider.Validate(main, main))
            {
                // Validate the others
                var others = _providers.Except(new[] { mainProvider });
                foreach (var other in others)
                {
                    var opt = other.GetResult(_args);
                    if (opt == null)
                    {
                        return false;
                    }
                    if (!other.Validate(opt, main))
                    {
                        return false;
                    }
                }
            }
            else
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Test if any arguments are active, so that we can warn users
        /// that these arguments have no effect on renewals.
        /// </summary>
        /// <returns></returns>
        public bool Active()
        {
            var mainProvider = _providers.OfType<IArgumentsProvider<MainArguments>>().First();
            var others = _providers.Except(new[] { mainProvider });
            foreach (var other in others)
            {
                var opt = other.GetResult(_args);
                if (opt != null && other.Active(opt))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Show current command line
        /// </summary>
        internal void ShowCommandLine()
        {
            try
            {
                var censoredArgs = new List<string>();
                var censor = false;
                var censorList = new List<string> { "key", "password", "secret", "token" };
                for (var i = 0; i < _args.Length; i++)
                {
                    if (!censor)
                    {
                        censoredArgs.Add(_args[i]);
                        censor = censorList.Any(c => _args[i].ToLower().StartsWith("--") && _args[i].ToLower().Contains(c));
                    }
                    else
                    {
                        censoredArgs.Add(new string('*', _args[i].Length));
                        censor = false;
                    }
                }
                var argsFormat = censoredArgs.Any() ? $"Arguments: {string.Join(" ", censoredArgs)}" : "No command line arguments provided";
                _log.Verbose(LogType.Screen | LogType.Event, argsFormat);
                _log.Information(LogType.Disk, argsFormat);
            } 
            catch (Exception ex)
            {
                _log.Warning("Error censoring command line: {ex}", ex.Message);
            }
        }

        /// <summary>
        /// Show command line arguments for the help function
        /// </summary>
        internal void ShowArguments()
        {
            Console.WriteLine();
            foreach (var providerGroup in _providers.GroupBy(p => p.Group).OrderBy(g => g.Key))
            {
                if (!string.IsNullOrEmpty(providerGroup.Key))
                {
                    Console.WriteLine($"# {providerGroup.Key}");
                    Console.WriteLine();
                }

                foreach (var provider in providerGroup)
                {
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine($"## {provider.Name}");
                    Console.ResetColor();
                    if (!string.IsNullOrEmpty(provider.Condition))
                    {
                        Console.Write($"``` [{provider.Condition}] ```");
                        if (provider.Default)
                        {
                            Console.WriteLine(" (default)");
                        }
                        else
                        {
                            Console.WriteLine();
                        }
                    }
                    Console.WriteLine("```");
                    foreach (var x in provider.Configuration)
                    {
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.Write($"   --{x.LongName}");
                        Console.WriteLine();
                        Console.ResetColor();
                        var step = 60;
                        var pos = 0;
                        var words = x.Description.Split(' ');
                        while (pos < words.Length)
                        {
                            var line = "";
                            while (pos < words.Length && line.Length + words[pos].Length + 1 < step)
                            {
                                line += " " + words[pos++];
                            }
                            if (!Console.IsOutputRedirected)
                            {
                                Console.SetCursorPosition(3, Console.CursorTop);
                            }
                            Console.WriteLine($" {line}");
                        }
                        Console.WriteLine();
                    }
                    Console.WriteLine("```");
                }
            }
        }
    }
}