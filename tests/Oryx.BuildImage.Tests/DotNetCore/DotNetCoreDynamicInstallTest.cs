﻿// --------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
// --------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.IO;
using Microsoft.Oryx.BuildScriptGenerator;
using Microsoft.Oryx.BuildScriptGenerator.Common;
using Microsoft.Oryx.BuildScriptGenerator.DotNetCore;
using Microsoft.Oryx.BuildScriptGenerator.Node;
using Microsoft.Oryx.Tests.Common;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Oryx.BuildImage.Tests
{
    [Trait("platform", "dotnet")]
    public class DotNetCoreDynamicInstallTest : SampleAppsTestBase
    {
        protected const string NetCoreApp11WebApp = "NetCoreApp11WebApp";
        protected const string NetCoreApp21WebApp = "NetCoreApp21.WebApp";
        protected const string NetCoreApp22WebApp = "NetCoreApp22WebApp";
        protected const string NetCoreApp30WebApp = "NetCoreApp30.WebApp";
        protected const string NetCoreApp30MvcApp = "NetCoreApp30.MvcApp";
        protected const string NetCoreApp31MvcApp = "NetCoreApp31.MvcApp";
        protected const string NetCoreApp50MvcApp = "NetCoreApp50MvcApp";
        protected const string NetCore6PreviewWebApp = "NetCore6PreviewWebApp";
        protected const string NetCore7PreviewMvcApp = "NetCore7PreviewMvcApp";
        protected const string DefaultWebApp = "DefaultWebApp";

        private DockerVolume CreateSampleAppVolume(string sampleAppName) =>
            DockerVolume.CreateMirror(Path.Combine(_hostSamplesDir, "DotNetCore", sampleAppName));

        private readonly string SdkVersionMessageFormat = "Using .NET Core SDK Version: {0}";

        public DotNetCoreDynamicInstallTest(ITestOutputHelper output) : base(output)
        {
        }

        [Theory, Trait("category", "githubactions")]
        [InlineData(NetCoreApp21WebApp, "2.1")]
        [InlineData(NetCoreApp31MvcApp, "3.1")]
        [InlineData(NetCoreApp50MvcApp, "5.0")]
        [InlineData(NetCore7PreviewMvcApp, "7.0.0-preview.7.22375.6")]
        public void BuildsApplication_ByDynamicallyInstallingSDKs_GithubActions(
            string appName,
            string runtimeVersion)
        {
            BuildsApplication_ByDynamicallyInstallingSDKs(
                appName, runtimeVersion, _restrictedPermissionsImageHelper.GetGitHubActionsBuildImage());
        }

        [Theory, Trait("category", "cli")]
        [InlineData(NetCoreApp21WebApp, "2.1")]
        [InlineData(NetCoreApp31MvcApp, "3.1")]
        [InlineData(NetCoreApp50MvcApp, "5.0")]
        [InlineData(NetCore7PreviewMvcApp, "7.0.0-preview.7.22375.6")]
        public void BuildsApplication_ByDynamicallyInstallingSDKs_Cli(
            string appName,
            string runtimeVersion)
        {
            BuildsApplication_ByDynamicallyInstallingSDKs(
                appName, runtimeVersion, _imageHelper.GetCliImage());
        }

        [Theory, Trait("category", "cli-buster")]
        [InlineData(NetCoreApp21WebApp, "2.1")]
        [InlineData(NetCoreApp31MvcApp, "3.1")]
        [InlineData(NetCoreApp50MvcApp, "5.0")]
        [InlineData(NetCore7PreviewMvcApp, "7.0.0-preview.7.22375.6")]
        public void BuildsApplication_ByDynamicallyInstallingSDKs_CliBuster(
            string appName,
            string runtimeVersion)
        {
            BuildsApplication_ByDynamicallyInstallingSDKs(
                appName, runtimeVersion, _imageHelper.GetCliImage("cli-buster"));
        }

        private void BuildsApplication_ByDynamicallyInstallingSDKs(
            string appName,
            string runtimeVersion,
            string imageName)
        {
            // Arrange
            var volume = CreateSampleAppVolume(appName);
            var appDir = volume.ContainerDir;
            var appOutputDir = "/tmp/output";
            var manifestFile = $"{appOutputDir}/{FilePaths.BuildManifestFileName}";
            var osTypeFile = $"{appOutputDir}/{FilePaths.OsTypeFileName}";
            var script = new ShellScriptBuilder()
                .AddDefaultTestEnvironmentVariables()
                .AddBuildCommand(
                $"{appDir} -i /tmp/int -o {appOutputDir} " +
                $"--platform {DotNetCoreConstants.PlatformName} --platform-version {runtimeVersion}")
                .AddFileExistsCheck($"{appOutputDir}/{appName}.dll")
                .AddFileExistsCheck(manifestFile)
                .AddFileExistsCheck(osTypeFile)
                .AddCommand($"cat {manifestFile}")
                .ToString();
            var majorPart = runtimeVersion.Split('.')[0];
            var expectedSdkVersionPrefix = $"{majorPart}.";

            // Act
            var result = _dockerCli.Run(new DockerRunArguments
            {
                ImageId = imageName,
                EnvironmentVariables = new List<EnvironmentVariable> { CreateAppNameEnvVar(appName) },
                Volumes = new List<DockerVolume> { volume },
                CommandToExecuteOnRun = "/bin/bash",
                CommandArguments = new[] { "-c", script }
            });

            // Assert
            RunAsserts(
                () =>
                {
                    Assert.True(result.IsSuccess);
                    Assert.Contains(string.Format(SdkVersionMessageFormat, expectedSdkVersionPrefix), result.StdOut);
                    Assert.Contains(
                        $"{ManifestFilePropertyKeys.DotNetCoreRuntimeVersion}=\"{runtimeVersion}",
                        result.StdOut);
                    Assert.Contains(
                        $"{ManifestFilePropertyKeys.DotNetCoreSdkVersion}=\"{expectedSdkVersionPrefix}",
                        result.StdOut);
                },
                result.GetDebugInfo());
        }

        [Fact, Trait("category", "githubactions")]
        public void DynamicInstall_ReInstallsSdk_IfSentinelFileIsNotPresent()
        {
            // Arrange
            var globalJsonTemplate = @"
            {
                ""sdk"": {
                    ""version"": ""#version#"",
                    ""rollForward"": ""Disable""
                }
            }";
            var globalJsonSdkVersion = "3.1.201";
            var globalJsonContent = globalJsonTemplate.Replace("#version#", globalJsonSdkVersion);
            var sentinelFile = $"{Constants.TemporaryInstallationDirectoryRoot}/{DotNetCoreConstants.PlatformName}/{globalJsonSdkVersion}/" +
                $"{SdkStorageConstants.SdkDownloadSentinelFileName}";
            var appName = NetCoreApp31MvcApp;
            var volume = CreateSampleAppVolume(appName);
            // Create a global.json in host's app directory so that it can be present in container directory
            File.WriteAllText(
                Path.Combine(volume.MountedHostDir, DotNetCoreConstants.GlobalJsonFileName),
                globalJsonContent);
            var appDir = volume.ContainerDir;
            var appOutputDir = "/tmp/output";
            var buildCmd = $"{appDir} -i /tmp/int -o {appOutputDir}";
            var script = new ShellScriptBuilder()
                .AddDefaultTestEnvironmentVariables()
                .AddBuildCommand(buildCmd)
                .AddFileExistsCheck($"{appOutputDir}/{appName}.dll")
                .AddFileExistsCheck(sentinelFile)
                .AddCommand($"rm -f {sentinelFile}")
                .AddBuildCommand(buildCmd)
                .AddFileExistsCheck(sentinelFile)
                .ToString();

            // Act
            var result = _dockerCli.Run(new DockerRunArguments
            {
                ImageId = _imageHelper.GetGitHubActionsBuildImage(),
                EnvironmentVariables = new List<EnvironmentVariable> { CreateAppNameEnvVar(appName) },
                Volumes = new List<DockerVolume> { volume },
                CommandToExecuteOnRun = "/bin/bash",
                CommandArguments = new[] { "-c", script }
            });

            // Assert
            RunAsserts(
                () =>
                {
                    Assert.True(result.IsSuccess);
                    Assert.Contains(string.Format(SdkVersionMessageFormat, globalJsonSdkVersion), result.StdOut);
                },
                result.GetDebugInfo());
        }

        [Fact, Trait("category", "githubactions")]
        public void BuildsApplication_IgnoresExplicitRuntimeVersionBasedSdkVersion_AndUsesSdkVersionSpecifiedInGlobalJson()
        {
            // Here we are testing building a 2.1 runtime version app with a 3.1 sdk version

            // Arrange
            var expectedSdkVersion = "3.1.201";
            var globalJsonTemplate = @"
            {
                ""sdk"": {
                    ""version"": ""#version#"",
                    ""rollForward"": ""Disable""
                }
            }";
            var globalJsonContent = globalJsonTemplate.Replace("#version#", expectedSdkVersion);
            var appName = NetCoreApp21WebApp;
            var runtimeVersion = "2.1";
            var volume = CreateSampleAppVolume(appName);
            var appDir = volume.ContainerDir;
            var appOutputDir = "/tmp/output";

            // Create a global.json in host's app directory so that it can be present in container directory
            File.WriteAllText(
                Path.Combine(volume.MountedHostDir, DotNetCoreConstants.GlobalJsonFileName),
                globalJsonContent);
            var manifestFile = $"{appOutputDir}/{FilePaths.BuildManifestFileName}";
            var osTypeFile = $"{appOutputDir}/{FilePaths.OsTypeFileName}";
            var script = new ShellScriptBuilder()
                .AddDefaultTestEnvironmentVariables()
                .AddBuildCommand(
                $"{appDir} -i /tmp/int -o {appOutputDir} " +
                $"--platform {DotNetCoreConstants.PlatformName} --platform-version {runtimeVersion} --log-file log.txt")
                .AddFileExistsCheck($"{appOutputDir}/{appName}.dll")
                .AddFileExistsCheck(manifestFile)
                .AddFileExistsCheck(osTypeFile)
                .AddCommand($"cat {manifestFile}")
                .ToString();

            // Act
            var result = _dockerCli.Run(new DockerRunArguments
            {
                ImageId = _imageHelper.GetGitHubActionsBuildImage(),
                EnvironmentVariables = new List<EnvironmentVariable> { CreateAppNameEnvVar(appName) },
                Volumes = new List<DockerVolume> { volume },
                CommandToExecuteOnRun = "/bin/bash",
                CommandArguments = new[] { "-c", script }
            });

            // Assert
            RunAsserts(
                () =>
                {
                    Assert.True(result.IsSuccess);
                    Assert.Contains(string.Format(SdkVersionMessageFormat, expectedSdkVersion), result.StdOut);
                    Assert.Contains(
                        $"{ManifestFilePropertyKeys.DotNetCoreRuntimeVersion}=\"{runtimeVersion}",
                        result.StdOut);
                    Assert.Contains(
                        $"{ManifestFilePropertyKeys.DotNetCoreSdkVersion}=\"{expectedSdkVersion}",
                        result.StdOut);
                },
                result.GetDebugInfo());
        }

        [Fact, Trait("category", "githubactions")]
        public void BuildsApplication_IgnoresRuntimeVersionBasedSdkVersion_AndUsesSdkVersionSpecifiedInGlobalJson()
        {
            // Here we are testing building a 2.1 runtime version app with a 3.1 sdk version

            // Arrange
            var expectedSdkVersion = "3.1.201";
            var globalJsonTemplate = @"
            {
                ""sdk"": {
                    ""version"": ""#version#"",
                    ""rollForward"": ""Disable""
                }
            }";
            var globalJsonContent = globalJsonTemplate.Replace("#version#", expectedSdkVersion);
            var appName = NetCoreApp21WebApp;
            var runtimeVersion = "2.1";
            var volume = CreateSampleAppVolume(appName);
            var appDir = volume.ContainerDir;
            var appOutputDir = "/tmp/output";

            // Create a global.json in host's app directory so that it can be present in container directory
            File.WriteAllText(
                Path.Combine(volume.MountedHostDir, DotNetCoreConstants.GlobalJsonFileName),
                globalJsonContent);
            var manifestFile = $"{appOutputDir}/{FilePaths.BuildManifestFileName}";
            var osTypeFile = $"{appOutputDir}/{FilePaths.OsTypeFileName}";
            var script = new ShellScriptBuilder()
                .AddDefaultTestEnvironmentVariables()
                .AddBuildCommand($"{appDir} -i /tmp/int -o {appOutputDir}")
                .AddFileExistsCheck($"{appOutputDir}/{appName}.dll")
                .AddFileExistsCheck(manifestFile)
                .AddFileExistsCheck(osTypeFile)
                .AddCommand($"cat {manifestFile}")
                .ToString();

            // Act
            var result = _dockerCli.Run(new DockerRunArguments
            {
                ImageId = _imageHelper.GetGitHubActionsBuildImage(),
                EnvironmentVariables = new List<EnvironmentVariable> { CreateAppNameEnvVar(appName) },
                Volumes = new List<DockerVolume> { volume },
                CommandToExecuteOnRun = "/bin/bash",
                CommandArguments = new[] { "-c", script }
            });

            // Assert
            RunAsserts(
                () =>
                {
                    Assert.True(result.IsSuccess);
                    Assert.Contains(string.Format(SdkVersionMessageFormat, expectedSdkVersion), result.StdOut);
                    Assert.Contains(
                        $"{ManifestFilePropertyKeys.DotNetCoreRuntimeVersion}=\"{runtimeVersion}",
                        result.StdOut);
                    Assert.Contains(
                        $"{ManifestFilePropertyKeys.DotNetCoreSdkVersion}=\"{expectedSdkVersion}",
                        result.StdOut);
                },
                result.GetDebugInfo());
        }

        [Fact, Trait("category", "githubactions")]
        public void BuildsApplication_UsingPreviewVersionOfSdk()
        {
            // Arrange
            var expectedSdkVersion = "7.0.100-preview.1.22110.4";
            var globalJsonTemplate = @"
            {
                ""sdk"": {
                    ""version"": ""#version#"",
                    ""rollForward"": ""Disable""
                }
            }";
            var globalJsonContent = globalJsonTemplate.Replace("#version#", expectedSdkVersion);
            var appName = NetCoreApp50MvcApp;
            var runtimeVersion = "5.0";
            var volume = CreateSampleAppVolume(appName);
            var appDir = volume.ContainerDir;
            var appOutputDir = "/tmp/output";

            // Create a global.json in host's app directory so that it can be present in container directory
            File.WriteAllText(
                Path.Combine(volume.MountedHostDir, DotNetCoreConstants.GlobalJsonFileName),
                globalJsonContent);

            var manifestFile = $"{appOutputDir}/{FilePaths.BuildManifestFileName}";
            var osTypeFile = $"{appOutputDir}/{FilePaths.OsTypeFileName}";
            var script = new ShellScriptBuilder()
                .AddDefaultTestEnvironmentVariables()
                .AddBuildCommand($"{appDir} -i /tmp/int -o {appOutputDir}")
                .AddFileExistsCheck($"{appOutputDir}/{appName}.dll")
                .AddFileExistsCheck(manifestFile)
                .AddFileExistsCheck(osTypeFile)
                .AddCommand($"cat {manifestFile}")
                .ToString();

            // Act
            var result = _dockerCli.Run(new DockerRunArguments
            {
                ImageId = _imageHelper.GetGitHubActionsBuildImage(),
                EnvironmentVariables = new List<EnvironmentVariable> { CreateAppNameEnvVar(appName) },
                Volumes = new List<DockerVolume> { volume },
                CommandToExecuteOnRun = "/bin/bash",
                CommandArguments = new[] { "-c", script }
            });

            // Assert
            RunAsserts(
                () =>
                {
                    Assert.True(result.IsSuccess);
                    Assert.Contains(string.Format(SdkVersionMessageFormat, expectedSdkVersion), result.StdOut);
                    Assert.Contains(
                        $"{ManifestFilePropertyKeys.DotNetCoreRuntimeVersion}=\"{runtimeVersion}",
                        result.StdOut);
                    Assert.Contains(
                        $"{ManifestFilePropertyKeys.DotNetCoreSdkVersion}=\"{expectedSdkVersion}",
                        result.StdOut);
                },
                result.GetDebugInfo());
        }

        [Fact, Trait("category", "githubactions")]
        public void BuildsAppAfterInstallingAllRequiredPlatforms()
        {
            // Arrange
            var appName = "dotNetCoreReactApp";
            var hostDir = Path.Combine(_hostSamplesDir, "multilanguage", appName);
            var volume = DockerVolume.CreateMirror(hostDir);
            var appDir = volume.ContainerDir;
            var appOutputDir = $"{appDir}/myoutputdir";
            var buildScript = new ShellScriptBuilder()
                .AddBuildCommand(
                $"{appDir} -o {appOutputDir} --platform {DotNetCoreConstants.PlatformName} --platform-version 3.1")
                .AddFileExistsCheck($"{appOutputDir}/dotNetCoreReactApp.dll")
                .AddDirectoryExistsCheck($"{appOutputDir}/ClientApp/build")
                .ToString();

            // Act
            var result = _dockerCli.Run(new DockerRunArguments
            {
                ImageId = _imageHelper.GetGitHubActionsBuildImage(),
                EnvironmentVariables = new List<EnvironmentVariable> { CreateAppNameEnvVar(appName) },
                Volumes = new List<DockerVolume> { volume },
                CommandToExecuteOnRun = "/bin/bash",
                CommandArguments = new[] { "-c", buildScript }
            });

            // Assert
            RunAsserts(
                () =>
                {
                    Assert.True(result.IsSuccess);
                    Assert.Contains("Using .NET Core SDK Version: ", result.StdOut);
                    Assert.Contains("react-scripts build", result.StdOut);
                },
                result.GetDebugInfo());
        }

        [Fact, Trait("category", "githubactions")]
        public void BuildsApplication_ByDynamicallyInstallingSDKs_IntoCustomDynamicInstallationDir()
        {
            // Here we are testing building a 2.1 runtime version app with a 3.1 sdk version

            // Arrange
            var expectedSdkVersion = "3.1.201";
            var globalJsonTemplate = @"
            {
                ""sdk"": {
                    ""version"": ""#version#"",
                    ""rollForward"": ""Disable""
                }
            }";
            var globalJsonContent = globalJsonTemplate.Replace("#version#", expectedSdkVersion);
            var appName = NetCoreApp21WebApp;
            var runtimeVersion = "2.1";
            var volume = CreateSampleAppVolume(appName);
            var appDir = volume.ContainerDir;
            var appOutputDir = "/tmp/output";
            var expectedDynamicInstallRootDir = "/foo/bar";
            // Create a global.json in host's app directory so that it can be present in container directory
            File.WriteAllText(
                Path.Combine(volume.MountedHostDir, DotNetCoreConstants.GlobalJsonFileName),
                globalJsonContent);
            var manifestFile = $"{appOutputDir}/{FilePaths.BuildManifestFileName}";
            var osTypeFile = $"{appOutputDir}/{FilePaths.OsTypeFileName}";
            var script = new ShellScriptBuilder()
                .AddDefaultTestEnvironmentVariables()
                .AddBuildCommand(
                $"{appDir} -i /tmp/int -o {appOutputDir} --dynamic-install-root-dir {expectedDynamicInstallRootDir}")
                .AddDirectoryExistsCheck(
                $"{expectedDynamicInstallRootDir}/{DotNetCoreConstants.PlatformName}/{expectedSdkVersion}")
                .AddFileExistsCheck($"{appOutputDir}/{appName}.dll")
                .AddFileExistsCheck(manifestFile)
                .AddFileExistsCheck(osTypeFile)
                .AddCommand($"cat {manifestFile}")
                .ToString();

            // Act
            var result = _dockerCli.Run(new DockerRunArguments
            {
                ImageId = _imageHelper.GetGitHubActionsBuildImage(),
                EnvironmentVariables = new List<EnvironmentVariable> { CreateAppNameEnvVar(appName) },
                Volumes = new List<DockerVolume> { volume },
                CommandToExecuteOnRun = "/bin/bash",
                CommandArguments = new[] { "-c", script }
            });

            // Assert
            RunAsserts(
                () =>
                {
                    Assert.True(result.IsSuccess);
                    Assert.Contains(string.Format(SdkVersionMessageFormat, expectedSdkVersion), result.StdOut);
                    Assert.Contains(
                        $"{ManifestFilePropertyKeys.DotNetCoreRuntimeVersion}=\"{runtimeVersion}",
                        result.StdOut);
                    Assert.Contains(
                        $"{ManifestFilePropertyKeys.DotNetCoreSdkVersion}=\"{expectedSdkVersion}",
                        result.StdOut);
                },
                result.GetDebugInfo());
        }

        [Theory, Trait("category", "vso-focal")]
        [InlineData(NetCoreApp21WebApp, "2.1", DotNetCoreSdkVersions.DotNetCore21SdkVersion)]
        public void BuildsApplication_SetLinksCorrectly_ByDynamicallyInstallingSDKs(
            string appName,
            string runtimeVersion,
            string sdkVersion)
        {
            // Arrange
            var volume = CreateSampleAppVolume(appName);
            var appDir = volume.ContainerDir;
            var appOutputDir = "/tmp/output";
            var manifestFile = $"{appOutputDir}/{FilePaths.BuildManifestFileName}";
            var osTypeFile = $"{appOutputDir}/{FilePaths.OsTypeFileName}";
            var preInstalledSdkLink = $"/home/codespace/.dotnet/sdk";
            var script = new ShellScriptBuilder()
                .AddDefaultTestEnvironmentVariables()
                .AddDirectoryExistsCheck($"/home/codespace/.dotnet/")
                .AddLinkExistsCheck($"{preInstalledSdkLink}/{DotNetCoreSdkVersions.DotNetCore31SdkVersion}")
                .AddLinkExistsCheck($"{preInstalledSdkLink}/{DotNetCoreSdkVersions.DotNet60SdkVersion}")
                .AddLinkDoesNotExistCheck($"{preInstalledSdkLink}/{sdkVersion}")
                .AddBuildCommand(
                $"{appDir} -i /tmp/int -o {appOutputDir} " +
                $"--platform {DotNetCoreConstants.PlatformName} --platform-version {runtimeVersion}")
                .AddFileExistsCheck($"{appOutputDir}/{appName}.dll")
                .AddDirectoryExistsCheck($"/opt/dotnet/{sdkVersion}")
                .AddLinkExistsCheck($"{preInstalledSdkLink}/{sdkVersion}")
                .AddFileExistsCheck(manifestFile)
                .AddFileExistsCheck(osTypeFile)
                .AddCommand($"cat {manifestFile}")
                .AddCommand("/home/codespace/.dotnet/dotnet --list-sdks")
                .ToString();
            var majorPart = runtimeVersion.Split('.')[0];
            var expectedSdkVersionPrefix = $"{majorPart}.";

            // Act
            var result = _dockerCli.Run(new DockerRunArguments
            {
                ImageId = _imageHelper.GetVsoBuildImage("vso-focal"),
                EnvironmentVariables = new List<EnvironmentVariable> { CreateAppNameEnvVar(appName) },
                Volumes = new List<DockerVolume> { volume },
                CommandToExecuteOnRun = "/bin/bash",
                CommandArguments = new[] { "-c", script }
            });

            // Assert
            RunAsserts(
                () =>
                {
                    Assert.True(result.IsSuccess);
                    Assert.Contains(string.Format(SdkVersionMessageFormat, expectedSdkVersionPrefix), result.StdOut);
                    Assert.Contains(
                        $"{ManifestFilePropertyKeys.DotNetCoreSdkVersion}=\"{expectedSdkVersionPrefix}",
                        result.StdOut);
                    Assert.Contains($"{DotNetCoreSdkVersions.DotNetCore31SdkVersion} [/home/codespace/.dotnet/sdk]", result.StdOut);
                    Assert.Contains($"{DotNetCoreSdkVersions.DotNet60SdkVersion} [/home/codespace/.dotnet/sdk]", result.StdOut);
                    Assert.Contains($"{sdkVersion} [/home/codespace/.dotnet/sdk]", result.StdOut);
                },
                result.GetDebugInfo());
        }

        public static TheoryData<string, string, string, string> SupportedVersionAndImageNameData
        {
            get
            {
                var data = new TheoryData<string, string, string, string>();
                var imageHelper = new ImageTestHelper();

                // stretch
                data.Add(
                    DotNetCoreRunTimeVersions.NetCoreApp11,
                    DotNetCoreSdkVersions.DotNetCore11SdkVersion,
                    NetCoreApp11WebApp,
                    imageHelper.GetGitHubActionsBuildImage());
                data.Add(
                    DotNetCoreRunTimeVersions.NetCoreApp22,
                    DotNetCoreSdkVersions.DotNetCore22SdkVersion,
                    NetCoreApp22WebApp,
                    imageHelper.GetGitHubActionsBuildImage());
                data.Add(
                    DotNetCoreRunTimeVersions.NetCoreApp30,
                    DotNetCoreSdkVersions.DotNetCore30SdkVersion,
                    NetCoreApp30WebApp,
                    imageHelper.GetGitHubActionsBuildImage());
                data.Add(
                    FinalStretchVersions.FinalStretchDotNetCoreApp31RunTimeVersion,
                    FinalStretchVersions.FinalStretchDotNetCore31SdkVersion,
                    NetCoreApp31MvcApp,
                    imageHelper.GetGitHubActionsBuildImage());
                data.Add(
                    DotNetCoreRunTimeVersions.NetCoreApp50,
                    DotNetCoreSdkVersions.DotNet50SdkVersion,
                    NetCoreApp50MvcApp,
                    imageHelper.GetGitHubActionsBuildImage());
                data.Add(
                    FinalStretchVersions.FinalStretchDotNetCoreApp60RunTimeVersion,
                    FinalStretchVersions.FinalStretchDotNet60SdkVersion,
                    NetCore6PreviewWebApp,
                    imageHelper.GetGitHubActionsBuildImage());
                data.Add(
                    DotNetCoreRunTimeVersions.NetCoreApp70,
                    DotNetCoreSdkVersions.DotNet70SdkVersion,
                    NetCore7PreviewMvcApp,
                    imageHelper.GetGitHubActionsBuildImage());

                //buster
                data.Add(
                    DotNetCoreRunTimeVersions.NetCoreApp21,
                    DotNetCoreSdkVersions.DotNetCore21SdkVersion,
                    NetCoreApp21WebApp,
                    imageHelper.GetGitHubActionsBuildImage("github-actions-buster"));
                data.Add(
                    DotNetCoreRunTimeVersions.NetCoreApp31,
                    DotNetCoreSdkVersions.DotNetCore31SdkVersion,
                    NetCoreApp31MvcApp,
                    imageHelper.GetGitHubActionsBuildImage("github-actions-buster"));
                data.Add(
                    DotNetCoreRunTimeVersions.NetCoreApp50,
                    DotNetCoreSdkVersions.DotNet50SdkVersion,
                    NetCoreApp50MvcApp,
                    imageHelper.GetGitHubActionsBuildImage("github-actions-buster"));
                data.Add(
                    DotNetCoreRunTimeVersions.NetCoreApp60,
                    DotNetCoreSdkVersions.DotNet60SdkVersion,
                    NetCore6PreviewWebApp,
                    imageHelper.GetGitHubActionsBuildImage("github-actions-buster"));
                data.Add(
                    DotNetCoreRunTimeVersions.NetCoreApp70,
                    DotNetCoreSdkVersions.DotNet70SdkVersion,
                    NetCore7PreviewMvcApp,
                    imageHelper.GetGitHubActionsBuildImage("github-actions-buster"));

                //bullseye
                data.Add(
                    DotNetCoreRunTimeVersions.NetCoreApp31,
                    DotNetCoreSdkVersions.DotNetCore31SdkVersion,
                    NetCoreApp31MvcApp,
                    imageHelper.GetGitHubActionsBuildImage("github-actions-bullseye"));
                data.Add(
                    DotNetCoreRunTimeVersions.NetCoreApp60,
                    DotNetCoreSdkVersions.DotNet60SdkVersion,
                    NetCore6PreviewWebApp,
                    imageHelper.GetGitHubActionsBuildImage("github-actions-bullseye"));
                data.Add(
                    DotNetCoreRunTimeVersions.NetCoreApp70,
                    DotNetCoreSdkVersions.DotNet70SdkVersion,
                    NetCore7PreviewMvcApp,
                    imageHelper.GetGitHubActionsBuildImage("github-actions-bullseye"));
                
                return data;
            }
        }

        [Theory, Trait("category", "githubactions")]
        [MemberData(nameof(SupportedVersionAndImageNameData))]
        public void BuildsApplication_AfterInstallingSupportedSdk(
            string runtimeVersion, 
            string sdkVersion, 
            string appName,
            string imageName)
        {
            var volume = CreateSampleAppVolume(appName);
            var appDir = volume.ContainerDir;
            var appOutputDir = "/tmp/output";
            var manifestFile = $"{appOutputDir}/{FilePaths.BuildManifestFileName}";
            var script = new ShellScriptBuilder()
                .AddDefaultTestEnvironmentVariables()
                .AddBuildCommand(
                $"{appDir} -i /tmp/int -o {appOutputDir} " +
                $"--platform {DotNetCoreConstants.PlatformName} --platform-version {runtimeVersion}")
                .AddFileExistsCheck(manifestFile)
                .AddCommand($"cat {manifestFile}")
                .ToString();

            // Act
            var result = _dockerCli.Run(new DockerRunArguments
            {
                ImageId = imageName,
                EnvironmentVariables = new List<EnvironmentVariable> { CreateAppNameEnvVar(appName) },
                Volumes = new List<DockerVolume> { volume },
                CommandToExecuteOnRun = "/bin/bash",
                CommandArguments = new[] { "-c", script }
            });

            // Assert
            RunAsserts(
                () =>
                {
                    Assert.True(result.IsSuccess);
                    Assert.Contains(string.Format(SdkVersionMessageFormat, sdkVersion), result.StdOut);
                    Assert.Contains(
                        $"{ManifestFilePropertyKeys.DotNetCoreRuntimeVersion}=\"{runtimeVersion}",
                        result.StdOut);
                    Assert.Contains(
                        $"{ManifestFilePropertyKeys.DotNetCoreSdkVersion}=\"{sdkVersion}",
                        result.StdOut);
                },
                result.GetDebugInfo());
        }

        public static TheoryData<string, string> UnsupportedVersionAndImageNameData
        {
            get
            {
                var data = new TheoryData<string, string>();
                var imageHelper = new ImageTestHelper();
                data.Add(DotNetCoreRunTimeVersions.NetCoreApp11, imageHelper.GetGitHubActionsBuildImage("github-actions-buster"));
                data.Add(DotNetCoreRunTimeVersions.NetCoreApp22, imageHelper.GetGitHubActionsBuildImage("github-actions-buster"));
                data.Add(DotNetCoreRunTimeVersions.NetCoreApp30, imageHelper.GetGitHubActionsBuildImage("github-actions-buster"));

                data.Add(DotNetCoreRunTimeVersions.NetCoreApp11, imageHelper.GetGitHubActionsBuildImage("github-actions-bullseye"));
                data.Add(DotNetCoreRunTimeVersions.NetCoreApp21, imageHelper.GetGitHubActionsBuildImage("github-actions-bullseye"));
                data.Add(DotNetCoreRunTimeVersions.NetCoreApp22, imageHelper.GetGitHubActionsBuildImage("github-actions-bullseye"));
                data.Add(DotNetCoreRunTimeVersions.NetCoreApp30, imageHelper.GetGitHubActionsBuildImage("github-actions-bullseye"));
                data.Add(DotNetCoreRunTimeVersions.NetCoreApp50, imageHelper.GetGitHubActionsBuildImage("github-actions-bullseye"));
                return data;
            }
        }

        [Theory, Trait("category", "githubactions")]
        [MemberData(nameof(UnsupportedVersionAndImageNameData))]
        public void DotnetFails_ToInstallUnsupportedSdk(string runtimeVersion, string imageName)
        {
            var appName = NetCoreApp21WebApp;

            var volume = CreateSampleAppVolume(appName);
            var appDir = volume.ContainerDir;
            var appOutputDir = "/tmp/output";
            var manifestFile = $"{appOutputDir}/{FilePaths.BuildManifestFileName}";
            var script = new ShellScriptBuilder()
                .AddDefaultTestEnvironmentVariables()
                .AddBuildCommand(
                $"{appDir} -i /tmp/int -o {appOutputDir} " +
                $"--platform {DotNetCoreConstants.PlatformName} --platform-version {runtimeVersion}")
                .AddFileExistsCheck(manifestFile)
                .AddCommand($"cat {manifestFile}")
                .ToString();

            // Act
            var result = _dockerCli.Run(new DockerRunArguments
            {
                ImageId = imageName,
                EnvironmentVariables = new List<EnvironmentVariable> { CreateAppNameEnvVar(appName) },
                Volumes = new List<DockerVolume> { volume },
                CommandToExecuteOnRun = "/bin/bash",
                CommandArguments = new[] { "-c", script }
            });

            // Assert
            RunAsserts(
                () =>
                {
                    Assert.False(result.IsSuccess);
                    Assert.Contains($"Error: Platform '{DotNetCoreConstants.PlatformName}' version '{runtimeVersion}' is unsupported.", result.StdErr);
                },
                result.GetDebugInfo());
        }
    }
}
