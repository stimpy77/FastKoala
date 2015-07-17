# Fast Koala
Enables build-time config transforms for various project types including web apps (but not web sites, and Azure is not supported at this time), with future plans to also ease config name management and add MSBuild scripts (Imports directives to custom .targets files) to a project.

Current status: Initial commit performs basic functionality for empty web apps (not sites) that need build-time transformations.

    Web.config
    Web.Debug.config
    Web.Release.config
    
.. become ..

    App_Config\Web.Base.config
    App_Config\Web.Debug.config
    App_Config\Web.Release.config
  
and Web.config at project root becomes transient (and should never be added to source control).

Initial commit also supports basic class libraries (which can have config files) and Windows apps (other than ClickOnce apps) that need to transform out to the bin\Debug or bin\Release directory as AssemblyName.exe.config.

In all cases, to use, right-click on the project node or the [Web|App].config in Solution Explorer and choose "Enable build-time transformations"

This project *does not* use automated unit tests. :(

###Critical Limitations###

Basic support for projects under source control is planned but not yet implemented. Please do not use if you are using source control, especially if you are using TFS.

Web sites are not supported and will never be supported.

Visual Studio 2015 and ASP.NET 5 are not yet supported and the latter might not ever be supported.

### How it works ###

This Visual Studio extension will modify your project by injecting a custom MSBuild target that invokes the TransformXml task with the custom config paths as parameters. It does not use NuGet and it does not import an external .targets file in order to support build-time transformations--at least, not at this time, these behaviors might be added down the road but there are several reasons to avoid any of that.

The complete and simple explanation of the core method of how this is accomplished is laid out in the following very useful resource from EdCharbeneau which upon reading it started this whole effort: https://gist.github.com/EdCharbeneau/9135216
