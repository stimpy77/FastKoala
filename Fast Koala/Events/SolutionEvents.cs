using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Events;
using Microsoft.VisualStudio.Shell.Interop;
using Wijits.FastKoala.Utilities;

namespace Wijits.FastKoala.Events
{
    public class SolutionEventsWrapper : IVsSolutionEvents
    {
        public event EventHandler AfterCloseSolution;
        public event EventHandler<AfterLoadProjectEventArgs> AfterLoadProject;
        public event EventHandler<AfterOpenProjectEventArgs> AfterOpenProject;
        public event EventHandler<AfterOpenSolutionEventArgs> AfterOpenSolution;
        public event EventHandler<BeforeCloseProjectEventArgs> BeforeCloseProject;
        public event EventHandler<BeforeCloseSolutionEventArgs> BeforeCloseSolution;
        public event EventHandler<BeforeUnloadProjectEventArgs> BeforeUnloadProject;
        public event EventHandler<QueryCloseProjectEventArgs> QueryCloseProject;
        public event EventHandler<QueryCloseSolutionEventArgs> QueryCloseSolution;
        public event EventHandler<QueryUnloadProjectEventArgs> QueryUnloadProject;

        int IVsSolutionEvents.OnAfterCloseSolution(object pUnkReserved)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            AfterCloseSolution?.Invoke(VsEnvironment.Dte.Solution, new EventArgs());
            return VSConstants.S_OK;
        }

        int IVsSolutionEvents.OnAfterLoadProject(IVsHierarchy pStubHierarchy, IVsHierarchy pRealHierarchy)
        {
            if (AfterLoadProject != null)
            {
                var project = pRealHierarchy.GetDteProject();
                AfterLoadProject(project, new AfterLoadProjectEventArgs
                {
                    StubHierarchy = pStubHierarchy,
                    RealHierarchy = pRealHierarchy,
                    Project = project
                });
            }
            return VSConstants.S_OK;
        }

        int IVsSolutionEvents.OnAfterOpenProject(IVsHierarchy pHierarchy, int fAdded)
        {
            if (AfterOpenProject != null)
            {
                var project = pHierarchy.GetDteProject();
                AfterOpenProject(project, new AfterOpenProjectEventArgs
                {
                    Hierarchy = pHierarchy,
                    Project = project,
                    Added = fAdded == 1
                });
            }
            return VSConstants.S_OK;
        }

        int IVsSolutionEvents.OnAfterOpenSolution(object pUnkReserved, int fNewSolution)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            AfterOpenSolution?.Invoke(VsEnvironment.Dte.Solution, new AfterOpenSolutionEventArgs
            {
                Solution = VsEnvironment.Dte.Solution,
                NewSolution = fNewSolution == 1
            });
            return VSConstants.S_OK;
        }

        int IVsSolutionEvents.OnBeforeCloseProject(IVsHierarchy pHierarchy, int fRemoved)
        {
            if (BeforeCloseProject != null)
            {
                var project = pHierarchy.GetDteProject();
                BeforeCloseProject(project, new BeforeCloseProjectEventArgs
                {
                    Project = project,
                    Hierarchy = pHierarchy,
                    Removed = fRemoved == 1
                });
            }
            return VSConstants.S_OK;
        }

        int IVsSolutionEvents.OnBeforeCloseSolution(object pUnkReserved)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            if (BeforeCloseSolution != null)
            {
                var solution = VsEnvironment.Dte.Solution;
                BeforeCloseSolution(solution, new BeforeCloseSolutionEventArgs
                {
                    Solution = solution
                });
            }
            return VSConstants.S_OK;
        }

        int IVsSolutionEvents.OnBeforeUnloadProject(IVsHierarchy pRealHierarchy, IVsHierarchy pStubHierarchy)
        {
            if (BeforeUnloadProject != null)
            {
                var project = pRealHierarchy.GetDteProject();
                BeforeUnloadProject(project, new BeforeUnloadProjectEventArgs
                {
                    Project = project,
                    RealHierarchy = pRealHierarchy,
                    StubHierarchy = pStubHierarchy
                });
            }
            return VSConstants.S_OK;
        }

        int IVsSolutionEvents.OnQueryCloseProject(IVsHierarchy pHierarchy, int fRemoving, ref int pfCancel)
        {
            if (QueryCloseProject != null)
            {
                var project = pHierarchy.GetDteProject();
                QueryCloseProject(project, new QueryCloseProjectEventArgs(pHierarchy, fRemoving == 1));
            }
            return VSConstants.S_OK;
        }

        int IVsSolutionEvents.OnQueryCloseSolution(object pUnkReserved, ref int pfCancel)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            if (QueryCloseSolution != null)
            {
                var solution = VsEnvironment.Dte.Solution;
                var eventArgs = new QueryCloseSolutionEventArgs(solution, ref pfCancel);
                QueryCloseSolution(solution, eventArgs);
                pfCancel = eventArgs.Cancel ? 1 : 0;
            }
            return VSConstants.S_OK;
        }

        int IVsSolutionEvents.OnQueryUnloadProject(IVsHierarchy pRealHierarchy, ref int pfCancel)
        {
            if (QueryUnloadProject != null)
            {
                var project = pRealHierarchy.GetDteProject();
                var eventArgs = new QueryUnloadProjectEventArgs
                {
                    Project = project,
                    Cancel = pfCancel == 1
                };
                QueryUnloadProject(project, eventArgs);
                pfCancel = eventArgs.Cancel ? 1 : 0;
            }
            return VSConstants.S_OK;
        }
    }
}
