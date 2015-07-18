# Fast Koala
Enables build-time config transforms for various project types including web apps (but not web sites, and Azure is not supported at this time), with future plans to also ease config name management and add MSBuild scripts (Imports directives to custom .targets files) to a project.

###Inline Build-Time Transformations###
This tool enables build-time transformations for tested web apps (not websites).

    Web.config
    Web.Debug.config
    Web.Release.config
    
.. become ..

    App_Config\Web.Base.config
    App_Config\Web.Debug.config
    App_Config\Web.Release.config
  
and Web.config at project root becomes transient (and should never be added to source control).

###Bin-Targeted Build-Time Transformations###
This tool also supports enabling build-time transformations for class library projects (which can have config files) and for Windows apps (other than ClickOnce apps) that need to transform out to the bin\Debug or bin\Release directory as AssemblyName.exe.config.

###How to use###
In all cases, to use, right-click on the project node or the [Web|App].config in Solution Explorer and choose "Enable build-time transformations". 

If a transform file (i.e. Web.Debug.config) has been deleted or removed, right-click on the base config file and choose "Add missing transforms".

####Setting the config directory####

For web apps, which use inline transformations and nested folders, the default folder name is "App_Config", but you can choose any name you like when prompted--you must keep that folder name forever--and you can use backslashes in the folder name to deeply nest the config files, i.e. "cfg\server". To leave the base config and its transforms in the project root, use simply a dot ("."). You can also share configs further up in the solution using "..", i.e. "..\CommonConfigs\Web".

###Limitations###

Web sites are not supported and will never be supported.

Visual Studio 2015 and ASP.NET 5 are not yet supported and the latter might not ever be supported.

### How it works ###

This Visual Studio extension will modify your project by injecting a custom MSBuild target that invokes the TransformXml task with the custom config paths as parameters. It does not use NuGet and it does not import an external .targets file in order to support build-time transformations--at least, not at this time, these behaviors might be added down the road but there are several reasons to avoid any of that.

The complete and simple explanation of the core method of how this is accomplished is laid out in the following very useful resource from EdCharbeneau which upon reading it started this whole effort: https://gist.github.com/EdCharbeneau/9135216

### Development notes ###

This project *does not* use automated unit tests in source code. :(
