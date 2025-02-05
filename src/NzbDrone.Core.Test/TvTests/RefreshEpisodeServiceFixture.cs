using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.TvTests
{
    [TestFixture]
    public class RefreshEpisodeServiceFixture : CoreTest<RefreshEpisodeService>
    {
        private List<Episode> _insertedEpisodes;
        private List<Episode> _updatedEpisodes;
        private List<Episode> _deletedEpisodes;
        private Tuple<Series, List<Episode>> _myFamilyPies;

        [OneTimeSetUp]
        public void TestFixture()
        {
            UseRealHttp();

            _myFamilyPies = Mocker.Resolve<SkyHookProxy>().GetSeriesInfo(77); // My Family Pies

            // Remove specials.
            _myFamilyPies.Item2.RemoveAll(v => v.SeasonNumber == 0);
        }

        private List<Episode> GetEpisodes()
        {
            return _myFamilyPies.Item2.JsonClone();
        }

        private Series GetSeries()
        {
            var series = _myFamilyPies.Item1.JsonClone();
            series.Seasons = new List<Season>();

            return series;
        }

        [SetUp]
        public void Setup()
        {
            _insertedEpisodes = new List<Episode>();
            _updatedEpisodes = new List<Episode>();
            _deletedEpisodes = new List<Episode>();

            Mocker.GetMock<IEpisodeService>().Setup(c => c.InsertMany(It.IsAny<List<Episode>>()))
                .Callback<List<Episode>>(e => _insertedEpisodes = e);

            Mocker.GetMock<IEpisodeService>().Setup(c => c.UpdateMany(It.IsAny<List<Episode>>()))
                .Callback<List<Episode>>(e => _updatedEpisodes = e);

            Mocker.GetMock<IEpisodeService>().Setup(c => c.DeleteMany(It.IsAny<List<Episode>>()))
                .Callback<List<Episode>>(e => _deletedEpisodes = e);
        }

        [Test]
        public void should_create_all_when_no_existing_episodes()
        {
            Mocker.GetMock<IEpisodeService>().Setup(c => c.GetEpisodeBySeries(It.IsAny<int>()))
                .Returns(new List<Episode>());

            Subject.RefreshEpisodeInfo(GetSeries(), GetEpisodes());

            _insertedEpisodes.Should().HaveSameCount(GetEpisodes());
            _updatedEpisodes.Should().BeEmpty();
            _deletedEpisodes.Should().BeEmpty();

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_update_all_when_all_existing_episodes()
        {
            Mocker.GetMock<IEpisodeService>().Setup(c => c.GetEpisodeBySeries(It.IsAny<int>()))
                .Returns(GetEpisodes());

            Subject.RefreshEpisodeInfo(GetSeries(), GetEpisodes());

            _insertedEpisodes.Should().BeEmpty();
            _updatedEpisodes.Should().HaveSameCount(GetEpisodes());
            _deletedEpisodes.Should().BeEmpty();
        }

        [Test]
        public void should_delete_all_when_all_existing_episodes_are_gone_from_datasource()
        {
            Mocker.GetMock<IEpisodeService>().Setup(c => c.GetEpisodeBySeries(It.IsAny<int>()))
                .Returns(GetEpisodes());

            Subject.RefreshEpisodeInfo(GetSeries(), new List<Episode>());

            _insertedEpisodes.Should().BeEmpty();
            _updatedEpisodes.Should().BeEmpty();
            _deletedEpisodes.Should().HaveSameCount(GetEpisodes());
        }

        [Test]
        public void should_delete_duplicated_episodes_based_on_season_episode_number()
        {
            var duplicateEpisodes = GetEpisodes().Skip(5).Take(2).ToList();

            Mocker.GetMock<IEpisodeService>().Setup(c => c.GetEpisodeBySeries(It.IsAny<int>()))
                .Returns(GetEpisodes().Union(duplicateEpisodes).ToList());

            Subject.RefreshEpisodeInfo(GetSeries(), GetEpisodes());

            _insertedEpisodes.Should().BeEmpty();
            _updatedEpisodes.Should().HaveSameCount(GetEpisodes());
            _deletedEpisodes.Should().HaveSameCount(duplicateEpisodes);
        }

        [Test]
        public void should_not_change_monitored_status_for_existing_episodes()
        {
            var series = GetSeries();
            series.Seasons = new List<Season>();
            series.Seasons.Add(new Season { SeasonNumber = 1, Monitored = false });

            var episodes = GetEpisodes();

            episodes.ForEach(e => e.Monitored = true);

            Mocker.GetMock<IEpisodeService>().Setup(c => c.GetEpisodeBySeries(It.IsAny<int>()))
                .Returns(episodes);

            Subject.RefreshEpisodeInfo(series, GetEpisodes());

            _updatedEpisodes.Should().HaveSameCount(GetEpisodes());
            _updatedEpisodes.Should().OnlyContain(e => e.Monitored == true);
        }

        [Test]
        public void should_not_set_monitored_status_for_old_episodes_to_false_if_episodes_existed()
        {
            var series = GetSeries();
            series.Seasons = new List<Season>();
            series.Seasons.Add(new Season { SeasonNumber = 1, Monitored = true });

            var episodes = GetEpisodes().OrderBy(v => v.SeasonNumber).ThenBy(v => v.EpisodeNumber).Take(5).ToList();

            episodes[1].AirDateUtc = DateTime.UtcNow.AddDays(-15);
            episodes[2].AirDateUtc = DateTime.UtcNow.AddDays(-10);
            episodes[3].AirDateUtc = DateTime.UtcNow.AddDays(1);

            var existingEpisodes = episodes.Skip(4).ToList();

            Mocker.GetMock<IEpisodeService>().Setup(c => c.GetEpisodeBySeries(It.IsAny<int>()))
                .Returns(existingEpisodes);

            Subject.RefreshEpisodeInfo(series, episodes);

            _insertedEpisodes = _insertedEpisodes.OrderBy(v => v.EpisodeNumber).ToList();

            _insertedEpisodes.Should().HaveCount(4);
            _insertedEpisodes[0].Monitored.Should().Be(true);
            _insertedEpisodes[1].Monitored.Should().Be(true);
            _insertedEpisodes[2].Monitored.Should().Be(true);
            _insertedEpisodes[3].Monitored.Should().Be(true);

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_set_monitored_status_for_old_episodes_to_false_if_no_episodes_existed()
        {
            var series = GetSeries();
            series.Seasons = new List<Season>();

            var episodes = GetEpisodes().OrderBy(v => v.AirDate).Take(4).ToList();

            episodes[1].AirDateUtc = DateTime.UtcNow.AddDays(-15);
            episodes[2].AirDateUtc = DateTime.UtcNow.AddDays(-10);
            episodes[3].AirDateUtc = DateTime.UtcNow.AddDays(1);

            Mocker.GetMock<IEpisodeService>().Setup(c => c.GetEpisodeBySeries(It.IsAny<int>()))
                .Returns(new List<Episode>());

            Subject.RefreshEpisodeInfo(series, episodes);

            _insertedEpisodes = _insertedEpisodes.OrderBy(v => v.AirDate).ToList();

            _insertedEpisodes.Should().HaveSameCount(episodes);
            _insertedEpisodes[0].Monitored.Should().Be(false);
            _insertedEpisodes[1].Monitored.Should().Be(false);
            _insertedEpisodes[2].Monitored.Should().Be(false);
            _insertedEpisodes[3].Monitored.Should().Be(true);

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_remove_duplicate_remote_episodes_before_processing()
        {
            Mocker.GetMock<IEpisodeService>().Setup(c => c.GetEpisodeBySeries(It.IsAny<int>()))
                .Returns(new List<Episode>());

            var episodes = Builder<Episode>.CreateListOfSize(5)
                                           .TheFirst(2)
                                           .With(e => e.SeasonNumber = 1)
                                           .With(e => e.EpisodeNumber = 1)
                                           .Build()
                                           .ToList();

            Subject.RefreshEpisodeInfo(GetSeries(), episodes);

            _insertedEpisodes.Should().HaveCount(episodes.Count - 1);
            _updatedEpisodes.Should().BeEmpty();
            _deletedEpisodes.Should().BeEmpty();
        }

        [Test]
        [Ignore("tpdb will always have an air date, if no air date then date of import is used as an air date, blame Gykes if not the case")]
        public void should_override_empty_airdate_for_direct_to_dvd()
        {
            var series = GetSeries();
            series.Status = SeriesStatusType.Ended;

            var episodes = Builder<Episode>.CreateListOfSize(10)
                                           .All()
                                           .With(v => v.AirDateUtc = null)
                                           .BuildListOfNew();

            Mocker.GetMock<IEpisodeService>().Setup(c => c.GetEpisodeBySeries(It.IsAny<int>()))
                .Returns(new List<Episode>());

            List<Episode> updateEpisodes = null;
            Mocker.GetMock<IEpisodeService>().Setup(c => c.InsertMany(It.IsAny<List<Episode>>()))
                .Callback<List<Episode>>(c => updateEpisodes = c);

            Subject.RefreshEpisodeInfo(series, episodes);

            updateEpisodes.Should().NotBeNull();
            updateEpisodes.Should().NotBeEmpty();
            updateEpisodes.All(v => v.AirDateUtc.HasValue).Should().BeTrue();
        }

        [Test]
        public void should_use_tba_for_episode_title_when_null()
        {
            Mocker.GetMock<IEpisodeService>().Setup(c => c.GetEpisodeBySeries(It.IsAny<int>()))
                .Returns(new List<Episode>());

            var episodes = Builder<Episode>.CreateListOfSize(1)
                                           .All()
                                           .With(e => e.Title = null)
                                           .Build()
                                           .ToList();

            Subject.RefreshEpisodeInfo(GetSeries(), episodes);

            _insertedEpisodes.First().Title.Should().Be("TBA");
        }

        [Test]
        public void should_update_air_date_when_multiple_episodes_air_on_the_same_day()
        {
            Mocker.GetMock<IEpisodeService>().Setup(c => c.GetEpisodeBySeries(It.IsAny<int>()))
                .Returns(new List<Episode>());

            var now = DateTime.UtcNow;
            var series = GetSeries();

            var episodes = Builder<Episode>.CreateListOfSize(2)
                                           .All()
                                           .With(e => e.SeasonNumber = 1)
                                           .With(e => e.AirDate = now.ToShortDateString())
                                           .With(e => e.AirDateUtc = now)
                                           .Build()
                                           .ToList();

            Subject.RefreshEpisodeInfo(series, episodes);

            _insertedEpisodes.First().AirDateUtc.Value.ToString("s").Should().Be(episodes.First().AirDateUtc.Value.ToString("s"));
            _insertedEpisodes.Last().AirDateUtc.Value.ToString("s").Should().Be(episodes.First().AirDateUtc.Value.AddMinutes(series.Runtime).ToString("s"));
        }

        [Test]
        public void should_not_update_air_date_when_more_than_three_episodes_air_on_the_same_day()
        {
            Mocker.GetMock<IEpisodeService>().Setup(c => c.GetEpisodeBySeries(It.IsAny<int>()))
                .Returns(new List<Episode>());

            var now = DateTime.UtcNow;
            var series = GetSeries();

            var episodes = Builder<Episode>.CreateListOfSize(4)
                                           .All()
                                           .With(e => e.SeasonNumber = 1)
                                           .With(e => e.AirDate = now.ToShortDateString())
                                           .With(e => e.AirDateUtc = now)
                                           .Build()
                                           .ToList();

            Subject.RefreshEpisodeInfo(series, episodes);

            _insertedEpisodes.Should().OnlyContain(e => e.AirDateUtc.Value.ToString("s") == episodes.First().AirDateUtc.Value.ToString("s"));
        }
    }
}
