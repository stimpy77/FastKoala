using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wijits.FastKoala.BuildScriptInjections
{
    public class BuildScriptInjector
    {
        /*
         * todo: inject this noise :D
         * 
         * <UsingTask TaskName="InvokePowerShell" TaskFactory="CodeTaskFactory" AssemblyFile="$(MSBuildToolsPath)\Microsoft.Build.Tasks.v$(MSBuildToolsVersion).dll">
            <ParameterGroup>
              <ScriptFile ParameterType="System.String" Required="true" />
            </ParameterGroup>
            <Task>
              <Reference Include="Microsoft.Build" />
              <Reference Include="System.Management.Automation" />
              <Reference Include="System.Xml" />
              <Using Namespace="System.Management.Automation" />
              <Using Namespace="System.Management.Automation.Runspaces" />
              <Using Namespace="Microsoft.Build.Evaluation" />
              <Code Type="Fragment" Language="cs"><![CDATA[
	          if (!ScriptFile.ToLower().EndsWith(".ps1")) return true;
	        Project project = ProjectCollection.GlobalProjectCollection.GetLoadedProjects(BuildEngine.ProjectFileOfTaskNode).FirstOrDefault()
		        ?? new Project(BuildEngine.ProjectFileOfTaskNode);
	        if (!ScriptFile.Contains("\\")) ScriptFile = project.DirectoryPath + "\\" + ScriptFile;
	        var runspaceConfig = RunspaceConfiguration.Create();
	        using (Runspace runspace = RunspaceFactory.CreateRunspace(runspaceConfig)) 
	        { 
		        runspace.Open();
		        var vars = new System.Text.StringBuilder();
		        foreach (ProjectProperty evaluatedProperty in project.AllEvaluatedProperties)
		        {
			        if (!evaluatedProperty.IsEnvironmentProperty)
			        {
				        var name = evaluatedProperty.Name;
				        var value = evaluatedProperty.EvaluatedValue;
				        if (!string.IsNullOrWhiteSpace(name))
				        {
					        if (!string.IsNullOrWhiteSpace(value))
					        {
						        if (value.ToLower() == "true" || value.ToLower() == "false") {
							        vars.AppendLine("$" + name + " = $" + value);
						        } else {
							        vars.AppendLine("$" + name + " = @\"\r\n" + value.Replace("\"", "\"\"") + "\r\n\"@");
						        }
					        }
				        }
			        }
		        }
		        using (RunspaceInvoke scriptInvoker = new RunspaceInvoke(runspace)) 
		        { 
		            using (Pipeline pipeline = runspace.CreatePipeline()) {
				        scriptInvoker.Invoke(vars.ToString()); 
				        BuildEngine.LogMessageEvent(new BuildMessageEventArgs(ScriptFile, "", "", MessageImportance.High));
				        pipeline.Commands.AddScript("& \"" + ScriptFile + "\""); 
				        try {
					        var results = pipeline.Invoke();
					        string soutput = "";
					        foreach (var result in results)
					        {
						        soutput += result.ToString();
					        }
					        BuildEngine.LogMessageEvent(new BuildMessageEventArgs(soutput, "", "", MessageImportance.High));
				        } catch (Exception e) {
					        BuildEngine.LogErrorEvent(new BuildErrorEventArgs("", "", ScriptFile, 0, 0, 0, 0, e.ToString(), "", "", DateTime.Now));
					        throw;
				        }
			        }
		        } 
	        }
	        return true;
        ]]></Code>
            </Task>
          </UsingTask>
          <ItemGroup>
            <AvailableItemName Include="InvokeBefore">
              <Visible>false</Visible>
            </AvailableItemName>
            <AvailableItemName Include="InvokeAfter">
              <Visible>false</Visible>
            </AvailableItemName>
          </ItemGroup>
          <Target Name="PSScriptsBefore" BeforeTargets="Build">
            <InvokePowerShell ScriptFile="%(InvokeBefore.Identity)" Condition="'@(InvokeBefore)' != ''" />
          </Target>
          <Target Name="PSScriptsAfter" AfterTargets="Build">
            <InvokePowerShell ScriptFile="%(InvokeAfter.Identity)" Condition="'@(InvokeAfter)' != ''" />
          </Target>
         */
    }
}
