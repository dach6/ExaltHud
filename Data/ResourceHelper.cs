using System;
using System.IO;

namespace ExaltHUD.Data;

public static class ResourceHelper
{
    public static readonly string Prefix = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExaltHUD_Data");
    public static string ObjectsXmlPath => Path.Combine(Prefix, "Objects.xml");
    public static string GroundTypesXmlPath => Path.Combine(Prefix, "GroundTypes.xml");
}
