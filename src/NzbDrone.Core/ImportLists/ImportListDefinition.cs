using System;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.ImportLists
{
    public class ImportListDefinition : ProviderDefinition
    {
        public bool EnableAutomaticAdd { get; set; }
        public MonitorTypes ShouldMonitor { get; set; }
        public int QualityProfileId { get; set; }
        public string RootFolderPath { get; set; }

        public override bool Enable => EnableAutomaticAdd;

        public ImportListStatus Status { get; set; }
        public ImportListType ListType { get; set; }
        public TimeSpan MinRefreshInterval { get; set; }
    }
}
