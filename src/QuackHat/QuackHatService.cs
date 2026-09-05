using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DuckGame;

namespace qUAckzak.Mod.QuackHat
{
    internal sealed class QuackHatService
    {
        private readonly string _catalogDirectory;
        private IReadOnlyList<QuackHatDefinition> _hats = Array.Empty<QuackHatDefinition>();
        private IReadOnlyList<QuackHatLoadFailure> _failures = Array.Empty<QuackHatLoadFailure>();

        public QuackHatService(string catalogDirectory)
        {
            _catalogDirectory = catalogDirectory;
        }

        public IReadOnlyList<QuackHatDefinition> Hats => _hats;

        public IReadOnlyList<QuackHatLoadFailure> Failures => _failures;

        public void Reload()
        {
            List<QuackHatDefinition> hats = new();
            List<QuackHatLoadFailure> failures = new();

            if (Directory.Exists(_catalogDirectory))
            {
                IEnumerable<string> packageDirectories;
                try
                {
                    packageDirectories = Directory
                        .EnumerateDirectories(_catalogDirectory)
                        .OrderBy(path => path, StringComparer.Ordinal)
                        .ToArray();
                }
                catch (Exception exception)
                {
                    packageDirectories = Array.Empty<string>();
                    failures.Add(new QuackHatLoadFailure
                    {
                        PackagePath = _catalogDirectory,
                        Message = $"Could not enumerate qUAckhat packages: {exception.Message}"
                    });
                }

                foreach (string packageDirectory in packageDirectories)
                {
                    try
                    {
                        QuackHatDefinition hat = QuackHatManifestLoader.Load(packageDirectory);
                        QuackHatAssetLoader.Load(hat);
                        hats.Add(hat);
                    }
                    catch (Exception exception)
                    {
                        failures.Add(new QuackHatLoadFailure
                        {
                            PackagePath = packageDirectory,
                            Message = exception.Message
                        });
                    }
                }
            }

            foreach (QuackHatDefinition oldHat in _hats)
            {
                QuackHatAssetLoader.Unload(oldHat);
            }

            _hats = hats;
            _failures = failures;
        }

        public void LogStatus()
        {
            DevConsole.Log(
                $"qUAckhat loaded {_hats.Count} hat package(s) with {_failures.Count} error(s).");

            foreach (QuackHatDefinition hat in _hats)
            {
                DevConsole.Log(
                    $"|LIME|qUAckhat loaded '{hat.Name}' ({hat.Components.Count} component(s)).");
            }

            foreach (QuackHatLoadFailure failure in _failures)
            {
                DevConsole.Log(
                    $"|RED|qUAckhat failed '{Path.GetFileName(failure.PackagePath)}': {failure.Message}");
            }
        }
    }
}
