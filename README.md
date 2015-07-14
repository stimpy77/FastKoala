# FastKoala
Enables build-time config transforms for various project types including web apps

Current status: Initial commit performs basic functionality for empty web sites that need build-time transformations.

  Web.config
  Web.Debug.config
  Web.Release.config
    
.. become ..

  App_Config\Web.Base.config
  App_Config\Web.Debug.config
  App_Config\Web.Release.config
  
and Web.config at project root becomes transient (and should never be added to source control).
