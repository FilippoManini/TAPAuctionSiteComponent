using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TAP21_22.AlarmClock.Interface;
using TAP21_22_AuctionSite.Interface;

namespace TAP21_22.AuctionSite.Testing {
    internal static class Configuration {
        /*internal const string StudentSpace = @"C:\Users\Mau\Source\Repos\StudentProjects\";
        internal const string CurrentStudent = "Tavella";
        internal static Dictionary<string, string> Implementations = new Dictionary<string, string>() {
            ["Viaggi"] = @"Viaggi\TAP_Project\SiteImplementation\bin\Debug\SiteImplementation.dll"
        };*/

        //MIEI 2018/19
        internal const string ImplementationAssembly = @"..\..\..\..\TAP21-22Manini\Manini\bin\Debug\net5.0\Manini.dll";

        internal const string ConnectionString = @"Data Source=.;Initial Catalog=DB_AS;Integrated Security=True;";

        // STUDENTI
        /*internal static string ImplementationAssembly =
            StudentSpace+Implementations[CurrentStudent];
        internal const string ConnectionString =
            @"Data Source=.;Initial Catalog="+CurrentStudent+"DB666;Integrated Security=True;";*/
    }

    public class TestAlarm : IAlarm {
        public event Action? RingingEvent;

        public void Dispose() { GC.SuppressFinalize(this); }

        internal void ExecuteActions() {
            RingingEvent?.Invoke();
        }
    }

    public class TestAlarmClock : IAlarmClock {
        private readonly TestAlarm _theAlarm = new();
        public readonly List<int> InstantiateAlarmCallData = new();

        public TestAlarmClock(int timezone) {
            Now = DateTime.UtcNow.AddHours(timezone);
            Timezone = timezone;
        }

        public int Timezone { get; }
        public DateTime Now { get; set; }

        public IAlarm InstantiateAlarm(int frequencyInMs) {
            InstantiateAlarmCallData.Add(frequencyInMs);
            return _theAlarm;
        }

        public void AddHours(double h) {
            Now = Now.AddHours(h);
        }

        public void AddSeconds(double s) {
            Now = Now.AddSeconds(s);
        }

        public void AddMinutes(double m) {
            Now = Now.AddMinutes(m);
        }

        public void RunRingingEvent() {
            _theAlarm.ExecuteActions();
        }
    }

    public class TestAlarmClockFactory : IAlarmClockFactory {
        private readonly Dictionary<int, TestAlarmClock> Clocks = new();

        public IAlarmClock InstantiateAlarmClock(int timezone) {
            if (Clocks.TryGetValue(timezone, out var clock))
                return clock;
            clock = new TestAlarmClock(timezone);
            Clocks[timezone] = clock;
            return clock;
        }
    }

    [TestFixture]
    public abstract class AuctionSiteTests {
        protected static readonly IHostFactory TheHostFactory;
        protected static readonly string TheConnectionString;

        static AuctionSiteTests() {
            var implementationAssembly = Assembly.LoadFrom(Configuration.ImplementationAssembly);
            var hostFactoryType = implementationAssembly.GetTypes().Single(t => typeof(IHostFactory).IsAssignableFrom(t));
            TheHostFactory = (Activator.CreateInstance(hostFactoryType) as IHostFactory)!;
            TheConnectionString = Configuration.ConnectionString;
        }

        protected IHost TheHost = null!;
        protected readonly TestAlarmClockFactory TheAlarmClockFactory = new();
        protected TestAlarmClock? TheClock;

        [SetUp]
        public void SetupHost() {
            TheHostFactory.CreateHost(TheConnectionString);
            TheHost = TheHostFactory.LoadHost(TheConnectionString, TheAlarmClockFactory);
        }

        protected ISite LoadSiteFromName(string siteName, int timezone) {
            TheClock = (TestAlarmClock?) TheAlarmClockFactory.InstantiateAlarmClock(timezone);
            return TheHostFactory.LoadHost(TheConnectionString, TheAlarmClockFactory).LoadSite(siteName);
        }
        protected ISite CreateAndLoadEmptySite(int timeZone, string siteName, int sessionExpirationTimeInSeconds, double minimumBidIncrement, out TestAlarmClock alarmClock) {
            TheHostFactory.LoadHost(TheConnectionString, TheAlarmClockFactory).CreateSite(siteName, timeZone, sessionExpirationTimeInSeconds, minimumBidIncrement);
            alarmClock = (TestAlarmClock)TheAlarmClockFactory.InstantiateAlarmClock(timeZone);
            return TheHostFactory.LoadHost(TheConnectionString, TheAlarmClockFactory).LoadSite(siteName);
        }
        protected ISite CreateAndLoadSite(int timeZone, string siteName, int sessionExpirationTimeInSeconds,
                                          double minimumBidIncrement, out TestAlarmClock alarmClock,
                                          List<string>? userNameList = null, string password = "puffo") {
            var newSite = CreateAndLoadEmptySite(timeZone, siteName, sessionExpirationTimeInSeconds, minimumBidIncrement, out alarmClock);
            if (null != userNameList)
                foreach (var user in userNameList)
                    newSite.CreateUser(user, password);
            return TheHostFactory.LoadHost(TheConnectionString, TheAlarmClockFactory).LoadSite(siteName);
        }

        protected ISite CreateAndLoadSite(int timeZone, string siteName, int sessionExpirationTimeInSeconds,
                                          double minimumBidIncrement, List<string> userNameList,
                                          List<string> loggedUserNameList, int delayBetweenLoginInSeconds,
                                          out List<ISession> sessionList, out TestAlarmClock alarmClock,
                                          string password = "puffo") {
            //Pre: loggedUserNameList non empty and included in userNameList
            var newSite = CreateAndLoadEmptySite(timeZone,siteName,sessionExpirationTimeInSeconds,minimumBidIncrement,out alarmClock);
            foreach (var user in userNameList)
                newSite.CreateUser(user, password);
            sessionList = new List<ISession>();
            var howManySessions = loggedUserNameList.Count;
            sessionList.Add(newSite.Login(loggedUserNameList[0], password)!);
            for (var i = 1; i < howManySessions; i++) {
                alarmClock.AddSeconds(delayBetweenLoginInSeconds);
                sessionList.Add(newSite.Login(loggedUserNameList[i], password)!);
            }
            return TheHostFactory.LoadHost(TheConnectionString, TheAlarmClockFactory).LoadSite(siteName);
        }

        protected static bool AreEquivalentSessions(ISession? session1, ISession? session2) {
            if (null==session2) {
                return session1 == null;
            }
            return CheckSessionValues(session1, session2.Id,session2.ValidUntil, session2.User);
        }

        protected static bool CheckSessionValues(ISession? session, string sessionId, DateTime validUntil, IUser user) {
            if (null == session)
                return false;
            return session.Id == sessionId && session.ValidUntil == validUntil && session.User.Equals(user);
        }

        protected bool CheckAuctionValues(IAuction auction, int auctionId, string sellerUsername, DateTime endsOn, string auctionDescription, double auctionCurrentPrice,
                                          string? currentWinnerUsername = null) {
            var currentWinner = auction.CurrentWinner();
            var currentWinnerIsCorrect = (currentWinnerUsername == null)? currentWinner == null: currentWinner != null&&currentWinner.Username == currentWinnerUsername;
            return auction.Id == auctionId && auction.Seller.Username == sellerUsername && SameDateTime(auction.EndsOn, endsOn) && auction.Description == auctionDescription &&
                   currentWinnerIsCorrect && Math.Abs(auction.CurrentPrice() - auctionCurrentPrice) < .001;
        }
        /// <summary>
        /// Equality up to seconds for dates
        /// Saving Date on DB may introduce approximations, so equality (as ticks) may not hold even for "same" date
        /// </summary>
        /// <param name="x">first date</param>
        /// <param name="y">second date</param>
        /// <returns>true iff date, hour, minute and second components are the same in both dates</returns>
        protected bool SameDateTime(DateTime x, DateTime y) {
            return x.Date == y.Date && x.Hour == y.Hour && x.Minute == y.Minute && x.Second == y.Second;
        }
    }
}