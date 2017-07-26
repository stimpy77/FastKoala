using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Wijits.FastKoala
{
    class MSBuildXmlTransformer
    {
        private Microsoft.Web.Publishing.Tasks.TransformXml _xfrm;
        public MSBuildXmlTransformer()
        {
            _xfrm = new Microsoft.Web.Publishing.Tasks.TransformXml();
        }

        public string Source
        {
            get { return _xfrm.Source; }
            set { _xfrm.Source = value; }
        }

        public string Transform
        {
            get { return _xfrm.Transform; }
            set { _xfrm.Transform = value; }
        }

        public string Destination
        {
            get { return _xfrm.Destination; }
            set { _xfrm.Destination = value; }
        }

        public bool Execute()
        {
            return _xfrm.Execute();
        }
    }
}
