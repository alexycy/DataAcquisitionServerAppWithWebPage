using Microsoft.Extensions.Configuration;
using System.IO;
using System.Xml.Linq;

namespace DataAcquisitionServerAppWithWebPage.Service
{

    public static class ConfigurationHelper
    {
       public static string GetPortValue(string xmlFileName, string parentNodeName, string childNodeName, string attributeName)
        {
            var doc = XDocument.Load(xmlFileName);
            var node = doc.Root.Element(parentNodeName).Element(childNodeName);
            return node.Attribute(attributeName).Value;
        }
    }

}
