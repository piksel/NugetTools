namespace Piksel.NugetTools.GetNugetDependants
{

    public struct Dependant
    {
        public string Id;
        public int Downloads;
        public string TheirVersion;
        public string OurVersion;
        public string OurPackage;

        public string DownloadsString
        {
            set
            {
                if (!int.TryParse(value, out Downloads))
                {
                    Downloads = -1;
                }
            }
        }

        public string OurDependency
        {
            set
            {
                var parts = value.Split(':');
                OurPackage = parts[0];
                OurVersion = parts[1];
            }
        }
    }
}
