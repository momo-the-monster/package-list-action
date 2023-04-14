﻿using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Extensions.FileSystemGlobbing;
using Nuke.Common;
using Nuke.Common.IO;
using Serilog;
using UnityPackageExporter.Dependency;
using UnityPackageExporter.Package;

namespace VRC.PackageManagement.Automation;

// Most of this code is adapted from https://github.com/Lachee/Unity-Package-Exporter, which is licensed under the MIT license.
partial class Build
{

    [Parameter("Unity Project Directory")]
    AbsolutePath unityPackageExportSource;
    
    [Parameter("Output .unitypackage file")]
    AbsolutePath unityPackageExportOutput;
    
    [Parameter("Adds an asset to the pack. Supports glob matching.", Separator = " ")]
    string[] assetPattern = new string[]{"**.*"};

    [Parameter("Adds an asset to the pack. Supports glob matching.", Separator = " ")]
    string[] excludePattern = new string[] { "Library/**.*"};
    
    [Parameter("Skips dependency analysis. Disabling this feature may result in missing assets in your packages.")]
    bool skipDep = false;
    
    [Parameter("Sets the root directory for the assets. Used in dependency analysis to only check files that could be potentially included.")]
    string assetRoot = "Assets";

    Target BuildUnityPackage => _ => _
        .Requires(() => unityPackageExportSource)
        .Requires(() => unityPackageExportOutput)
        .Executes( async () =>
        {
            Log.Information($"Packing {unityPackageExportSource}");
            
            // Make the output file (touch it) so we can exclude
            await File.WriteAllBytesAsync(unityPackageExportOutput, new byte[0]);

            Stopwatch timer = Stopwatch.StartNew();
            using DependencyAnalyser analyser = !skipDep ? await DependencyAnalyser.CreateAsync(unityPackageExportSource / assetRoot, excludePattern) : null;
            using Packer packer = new Packer(unityPackageExportSource, unityPackageExportOutput);

            // Match all the assets we need
            Matcher assetMatcher = new Matcher();
            assetMatcher.AddIncludePatterns(assetPattern);
            assetMatcher.AddExcludePatterns(excludePattern);
            assetMatcher.AddExclude(unityPackageExportOutput);
            
            var matchedAssets = assetMatcher.GetResultsInFullPath(unityPackageExportSource);

            Assert.True(matchedAssets.Count() > 0, "No assets matched the pattern. Please check your pattern and try again.");
            
            Log.Information($"Found {matchedAssets.Count()} matched assets");
            
            
            await packer.AddAssetsAsync(matchedAssets);

            if (!skipDep)
            {
                var results = await analyser.FindDependenciesAsync(matchedAssets);
                await packer.AddAssetsAsync(results);
            }

            // Finally flush and tell them we done
            Log.Information($"Finished Packing in {timer.ElapsedMilliseconds}ms to {packer.OutputPath}");
            await packer.FlushAsync();
        });
}