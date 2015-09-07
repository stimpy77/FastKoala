# Fast Koala
Enables build-time config transforms for various project types including ASP.NET 4.6-or-below web apps (but not web sites). It also supports adding an unlimited number of PowerShell scripts with the MSBuild project properties fully exposed, executing either before build or after build, as well as add MSBuild scripts (Imports directives to custom .targets files) to a project.

There are future plans to also ease config name management.

You **do not** need Fast Koala to be installed once its changes have been applied to a project. A TFS build server would need no knowledge of Fast Koala for any of Fast Koala's changes to be effective.

### Video Demo

(Click to watch)

[![Fast Koala v1.1.22](https://j.gifs.com/vW81x4.gif)](http://youtu.be/fJaAGwVxSQA)

### "Build-time" means F5

All references to "build-time" refer to F6 (Build) or F5 ([Build and] Debug). This means that you can finally test web apps with different configuration transformations applied *without* publishing, you can simply select the configuration and hit F5.

### Inline Build-Time Transformations
This tool enables build-time transformations for ASP.NET 4.6-or-below web apps (not websites), including ASP.NET MVC 5.

    Web.config
    Web.Debug.config
    Web.Release.config
    
.. become ..

    App_Config\Web.Base.config
    App_Config\Web.Debug.config
    App_Config\Web.Release.config
  
and Web.config at project root becomes transient (and should never be added to source control). **Web.config is created upon build.**

<sub><sup>(This was a feature I and many others always wanted from other extensions such as Slow Cheetah or Config Transformations.)</sup></sub>

### Bin-Targeted Build-Time Transformations###
This tool also supports enabling build-time transformations for class library projects (which can have config files) and for Windows apps (other than ClickOnce apps -- support for ClickOnce is coming but will use Inline Transformations) that need to transform out to the bin\Debug or bin\Release directory as AssemblyName.exe.config. For App.config and its transform files **there is no App_Config (or other chosen name) folder.** The App.config in the root directory is transformed upon build in the bin directory.

### Where to get it
You can download the official current release from the gallery here:
https://visualstudiogallery.msdn.microsoft.com/7bc82ddf-e51b-4bb4-942f-d76526a922a0

### How to use
In all cases, to use, right-click on the project node or the [Web|App].config in Solution Explorer and choose "Enable build-time transformations". 

If a transform file (i.e. Web.Debug.config) has been deleted or removed, right-click on the base config file and choose "Add missing transforms".

#### Setting the config directory

For web apps, which use inline transformations in a nested folder, the default folder name is "App_Config", but you can choose any name you like when prompted--you must keep that folder name unless you edit the project file--and you can use backslashes in the folder name to deeply nest the config files, i.e. "cfg\server". To leave the base config and its transforms in the project root, use simply a dot ("."). You can also share configs further up in the solution using "..", i.e. "..\CommonConfigs\Web".

### Limitations

Web sites are not supported and will never be supported.

ASP.NET 5 is not supported; it might not ever be supported.

### How build-time transformations work

This Visual Studio extension will modify your project by injecting a custom MSBuild target that invokes the TransformXml task with the custom config paths as parameters. It does not use NuGet and it does not import an external .targets file in order to support build-time transformations--at least, not at this time, these behaviors might be added down the road but there are several reasons to avoid any of that.

The complete and simple explanation of the core method of how this is accomplished is laid out in the following very useful resource from EdCharbeneau which upon reading it started this whole effort: https://gist.github.com/EdCharbeneau/9135216

### NuGet packages and other caveats

NuGet packages or other automated tasks that make tweaks to the web.config will need to be managed more carefully after Fast Koala is applied to a project. 

#### If you know changes will be made to web.config

1. Build the project first to generate the Web.config before applying the package.
2. Back up the Web.config file to create a copy that you can use below
3. Apply the package
4. Perform a diff (use WinMerge or Beyond Compare) between the backup made in #2 and the Web.config as it is now. Manually observe the changes and migrate these changes to the Web.Base.config file.

#### If you are an automation author

The `InlineAppCfgTransforms` and `AppConfigBaseFileFullPath` MSBuild properties in the .csproj/.vbproj file is available for NuGet authors to modify the Web.config if Fast Koala's "Enable Build-Time transfomations" has been applied. `InlineAppCfgTransforms` identifies whether inline transformations has been applied. `AppConfigBaseFileFullPath` ultimately consists of:

    $(MSBuildProjectDirectory)\$(ConfigDir)\$(AppCfgType).Base.config

You will need to either evaluate the MSBuild project property or load the project's XML and parse these properties out yourself.
    
### Adding Build Scripts

Fast Koala also supports adding build scripts, such as PowerShell scripts, NodeJS scripts, and project extensions (.targets files). To use this feature, right-click on the project node or a project folder in Solution Explorer and choose Add -> Build Script -> PowerShell Script (.ps1) or whichever script type you need. Scripts added with Fast Koala have the added advantage of having the MSBuild project properties exposed to the script runtime engine.

Additional script types are planned in the future.

### Development notes

This project *does not* use automated unit tests in source code. :(

You will need the Visual Studio SDK. The project is a VS2013 project and debugs in VS2013; runs in VS2015 just the same when published. You’ll need to set up Experimental debugging. http://stackoverflow.com/a/9281921/11398 
