using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Xml;
using System.Linq;
using System.Threading.Tasks;
using System.Text;

namespace Piksel.NugetTools.GetNugetDependants
{

    internal static class Program
    {
        const string apiBase = "https://packages.nuget.org/v1/FeedService.svc/Packages";
        const string dependantQuery = "?$filter=IsLatestVersion and substringof('{0}', Dependencies)&$orderby=DownloadCount desc";
        const string packageQuery = "?$filter='{0}' eq Id";

        private static XmlParserContext xmlContext;
        private static XmlDocument xmlDoc;
        private static XmlNamespaceManager xmlns;
        private static string targetPackage;
        private static readonly HttpClient http = new HttpClient();

        static string GetPackageUri(string packageId)
            => string.Concat(apiBase, string.Format(packageQuery, packageId));

        static string GetDependantsUri(string packageId)
            => string.Concat(apiBase, string.Format(dependantQuery, packageId));

        static string GetDependantsCountUri(string packageId)
            => string.Concat(apiBase, "/$count", string.Format(dependantQuery, packageId));

        static readonly List<Dependant> dependants = new List<Dependant>();

        static async Task<int> Main(string[] args) 
            => ParseArgs(args)
            && InitXml()
            && await FindDependants()
            && await WriteMarkDown()
            ? 0 : 1;

        private static bool ParseArgs(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Error: Missing argument PackageId");
                Usage();
                return false;
            }
            else
            {
                targetPackage = args[0];
                return true;

            }
        }

        private static bool InitXml()
        {
            try
            {
                var xmlSettings = new XmlReaderSettings { NameTable = new NameTable() };
                xmlns = new XmlNamespaceManager(xmlSettings.NameTable);
                xmlns.AddNamespace(string.Empty, "http://www.w3.org/2005/Atom");
                xmlns.AddNamespace("a", "http://www.w3.org/2005/Atom");
                xmlns.AddNamespace("d", "http://schemas.microsoft.com/ado/2007/08/dataservices");
                xmlns.AddNamespace("m", "http://schemas.microsoft.com/ado/2007/08/dataservices/metadata");
                xmlns.AddNamespace("georss", "http://www.georss.org/georss");
                xmlns.AddNamespace("gml", "http://www.opengis.net/gml");
                xmlContext = new XmlParserContext(null, xmlns, "", XmlSpace.Default);
                xmlDoc = new XmlDocument();
                return true;
            }
            catch (Exception x)
            {
                Console.WriteLine($"Failed to initialize XML Parser: {x.GetType().Name}: {x.Message}");
                return false;
            }
        }

        private static async Task<bool> WriteMarkDown()
        {
            var fileName = $"{targetPackage}-dependants.md";
            Console.Write($"Writing results to output file \"{fileName}\"... ");
            try
            {
                using (var sw = new StreamWriter(fileName, false, new UTF8Encoding(false)))
                {
                    await sw.WriteLineAsync($"# NuGet packages depending on {targetPackage}:");
                    foreach (var d in dependants)
                    {
                        await sw.WriteAsync($" - [{d.Id}](https://www.nuget.org/packages/{d.Id}/)");
                        var depPackage = d.OurPackage != targetPackage
                            ? $"**{d.OurPackage}** " : "";
                        await sw.WriteLineAsync($" v{d.TheirVersion} => {depPackage}`{d.OurVersion}`");
                    }
                }
                Console.WriteLine("OK");
                return true;
            }
            catch (Exception x)
            {
                Console.WriteLine($"Failed!\n{x.GetType().Name}: {x.Message}");
                return false;
            }
        }

        private static async Task<bool> FindDependants()
        {

            Console.Write($"Querying for package {targetPackage}... ");

            if (!await LoadXmlFromUri(GetPackageUri(targetPackage)))
            {
                return false;
            }

            Console.WriteLine("Found versions: " + string.Join(", ", ParseVersions()));

            Console.Write($"Querying for dependant count... ");
            if (!(await GetIntFromUri(GetDependantsCountUri(targetPackage)) is int dependantCount))
            {
                return false;
            }

            Console.WriteLine($"{dependantCount} package(s).");

            var queryCount = Math.Ceiling((double)dependantCount / 100);

            var offset = 0;
            var depUri = GetDependantsUri(targetPackage);

            while (true)
            {
                Console.Write($"Querying for dependants [{++offset}/{queryCount}]... ");

                if (!await LoadXmlFromUri(depUri))
                {
                    return false;
                }

                Console.WriteLine("OK");

                dependants.AddRange(ParseDependants());

                var nextLinkNode = xmlDoc.SelectSingleNode("/a:feed/a:link[@rel='next']", xmlns);

                if (nextLinkNode == null)
                {
                    break;
                }
                else
                {
                    depUri = Uri.UnescapeDataString(nextLinkNode.Attributes["href"].Value);
                }
            }

            return true;
            
        }

        private static async Task<HttpResponseMessage> TryGetResult(string uri)
        {
            var res = await http.GetAsync(uri);
            if (!res.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed!\nGot error {(int)res.StatusCode}: {res.ReasonPhrase}. Aborting.");
                return null;
            }

            return res;
        }

        private static async Task<bool> LoadXmlFromUri(string uri)
        {
            if (await TryGetResult(uri) is HttpResponseMessage res)
            {
                using (var stream = await res.Content.ReadAsStreamAsync())
                {
                    xmlDoc.Load(stream);
                    return true;
                }
            }
            return false;
        }

        private static async Task<int?> GetIntFromUri(string uri)
            => await TryGetResult(uri) is HttpResponseMessage res
            && int.TryParse(await res.Content.ReadAsStringAsync(), out int result)
                ? result : (int?)null;

        private static string[] ParseVersions()
            => xmlDoc.SelectNodes("/a:feed/a:entry", xmlns).Cast<XmlNode>()
                .Select(entry => entry.SelectSingleNode("m:properties/d:Version", xmlns).InnerText)
                .ToArray();

        private static IEnumerable<Dependant> ParseDependants()
            => xmlDoc.SelectNodes("/a:feed/a:entry", xmlns).Cast<XmlNode>()
                .Select(entry => new Dependant() {
                    Id = entry.SelectSingleNode("m:properties/d:Id", xmlns).InnerText,
                    DownloadsString = entry.SelectSingleNode("m:properties/d:DownloadCount", xmlns).InnerText,
                    TheirVersion = entry.SelectSingleNode("m:properties/d:Version", xmlns).InnerText,
                    OurDependency = entry.SelectSingleNode("m:properties/d:Dependencies", xmlns).InnerText
                        .Split('|')
                        .First(d => d.Contains(targetPackage))

                });

        private static void Usage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("GetNugetDependants <PackageId>");
        }
    }
}
