using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TAP21_22.AlarmClock.Interface;
using TAP21_22_AuctionSite.Interface;
using Manini.Data;

namespace Manini.Local
{
    public class HostFactory : IHostFactory
    {
        public void CreateHost(string connectionString)
        {
            if (connectionString == null)
                throw new AuctionSiteArgumentNullException("connectionString is null");

            using (var c = new ASTapDbContext(connectionString))
            {
                try
                {
                    c.Database.EnsureDeleted();
                    c.Database.EnsureCreated();
                    c.SaveChanges();
                }
                catch (SqlException e)
                {
                    throw new AuctionSiteUnavailableDbException("BadConnectionString", e); 
                }
            }
        }

        public IHost LoadHost(string connectionString, IAlarmClockFactory alarmClockFactory)
        {
            if (connectionString == null)
                throw new AuctionSiteArgumentNullException("connectionString is null");
            if(alarmClockFactory == null)
                throw new AuctionSiteArgumentNullException("alarmClockFactory is null");

            using (var c = new ASTapDbContext(connectionString))
            {
                if (!c.Database.CanConnect())
                    throw new AuctionSiteUnavailableDbException("BadConnectionString");
            }
            
            return new Host(connectionString, alarmClockFactory);
        }
    }

    public class Host : IHost
    {
        public string ConnectionString { get; set; }
        public IAlarmClockFactory AlarmClockFactory { get; set; } 

        public Host(string connectionString, IAlarmClockFactory alarmClockFactory)
        {
            ConnectionString = connectionString;
            AlarmClockFactory = alarmClockFactory;
        }

        public void CreateSite(string name, int timezone, int sessionExpirationTimeInSeconds, double minimumBidIncrement)
        {
            NameVerify(name);

            if (timezone < DomainConstraints.MinTimeZone)
                throw new AuctionSiteArgumentOutOfRangeException("timezone too small");
            if (timezone > DomainConstraints.MaxTimeZone)
                throw new AuctionSiteArgumentOutOfRangeException("timezone too large");

            if(sessionExpirationTimeInSeconds < 0)
                throw new AuctionSiteArgumentOutOfRangeException("sessionExpirationTimeInSeconds is not positive");
            if (minimumBidIncrement < 0)
                throw new AuctionSiteArgumentOutOfRangeException("minimumBidIncrement is not positive");

            Site site = new Site(name, timezone, sessionExpirationTimeInSeconds, minimumBidIncrement);

            using (var c = new ASTapDbContext(ConnectionString))
            {
                if (!c.Database.CanConnect())
                    throw new AuctionSiteUnavailableDbException("BadConnectionString");

                var siteS = c.Sites.Select(s => s.Name).ToList();
                foreach (var s in siteS)
                {
                    if (s == name)
                        throw new AuctionSiteNameAlreadyInUseException($"{nameof(name)}: this site is already exists");
                }

                c.Sites.Add(site);
                c.SaveChanges(); 
            }
        }

        //Fornisce i nomi e i fusi orari corrispondenti dei siti gestiti.
        public IEnumerable<(string Name, int TimeZone)> GetSiteInfos()
        {
            using (var c = new ASTapDbContext(ConnectionString))
            {
                if (!c.Database.CanConnect())
                    throw new AuctionSiteUnavailableDbException("BadConnectionString");

                List <Site> sites;

                try
                {
                    sites = c.Sites.ToList();
                }
                catch (ArgumentNullException e) 
                {
                    throw new AuctionSiteUnavailableDbException("sites is null", e);
                }

                foreach (var s in sites)
                {
                    yield return (s.Name, s.Timezone);
                }
            }
        }

        //Restituisce l'oggetto ISite corrispondente a un sito esistente.
        public ISite LoadSite(string name)
        {
            NameVerify(name);

            using (var c = new ASTapDbContext(ConnectionString))
            {
                if (!c.Database.CanConnect())
                    throw new AuctionSiteUnavailableDbException("BadConnectionString");

                try
                {
                    var s = c.Sites.SingleOrDefault(s => s.Name == name);
                    if(s == null)
                        throw new AuctionSiteInexistentNameException($"{nameof(name)}: this site not exists");

                    IAlarmClock alarmClock = AlarmClockFactory.InstantiateAlarmClock(s.Timezone); //quando carico il sito istanzio anche l'orologio 
                    alarmClock.InstantiateAlarm(5 * 60 * 1000);

                    return new LSite(s.Name, s.Timezone, s.SessionExpirationInSeconds, s.MinimumBidIncrement, ConnectionString, alarmClock);
                }
                catch (InvalidOperationException e)
                {
                    throw new AuctionSiteInvalidOperationException(e.Message, e);
                }
                catch (ArgumentNullException e)
                {
                    throw new AuctionSiteInexistentNameException(e.Message, e.Message, e);
                }
            }
        }

        //FUNZIONI INTERNE
        private static void NameVerify(string name)
        {
            if (name == null)
                throw new AuctionSiteArgumentNullException("name is null");
            if (name == String.Empty)
                throw new AuctionSiteArgumentException("name is Empty");
            if (name.Length < DomainConstraints.MinSiteName)
                throw new AuctionSiteArgumentException("name length too small");
            if (name.Length > DomainConstraints.MaxSiteName)
                throw new AuctionSiteArgumentException("name length too large");
        }
    }

    public class LSite : ISite
    {
        public string Name { get; }
        public int Timezone { get; } //Il fuso orario del sito dell'asta.
        public int SessionExpirationInSeconds { get; } //Il numero di secondi necessari per il timeout della sessione di un utente inattivo.
        public double MinimumBidIncrement { get; } //L'importo minimo consentito come incremento (dal prezzo di partenza) per un'offerta
        
        public string ConnectionString { get; }
        public IAlarmClock AlarmClock { get; } 

        public LSite(string name, int timezone, int sessionExpirationInSeconds, double minimumBidIncrement, string connectionString, IAlarmClock alarmClock)
        {
            Name = name;
            Timezone = timezone;
            SessionExpirationInSeconds = sessionExpirationInSeconds;
            MinimumBidIncrement = minimumBidIncrement;

            ConnectionString = connectionString;
            AlarmClock = alarmClock;
        }

        //Fornisce tutti gli utenti del sito.
        public IEnumerable<IUser> ToyGetUsers()
        {
            List<LUser> ul = new List<LUser>();
            using (var c = new ASTapDbContext(ConnectionString))
            {
                if (!c.Database.CanConnect())
                    throw new AuctionSiteUnavailableDbException("BadConnectionString");

                try
                {
                    var site = c.Sites.SingleOrDefault(s => s.Name == Name);
                    if (site == null)
                        throw new AuctionSiteInvalidOperationException($"{nameof(Name)}: this site not exists");

                    var ud = c.Users.Where(u => u.SiteUser.Name == Name);

                    if (ud == null) return ul;

                    foreach (var u in ud)
                    {
                        ul.Add(new LUser(u.Username, site.SiteId, ConnectionString, AlarmClock) );
                    }

                    return ul;
                }
                catch (InvalidOperationException e)
                {
                    throw new AuctionSiteInvalidOperationException(e.Message, e);
                }
                catch (ArgumentNullException e)
                {
                    throw new AuctionSiteArgumentNullException(e.Message, e);
                }
            }
        }

        //Restituisce tutte le sessioni del sito.
        public IEnumerable<ISession> ToyGetSessions()
        {
            using (var c = new ASTapDbContext(ConnectionString))
            {
                if (!c.Database.CanConnect())
                    throw new AuctionSiteUnavailableDbException("BadConnectionString");

                try
                {
                    var site = c.Sites.SingleOrDefault(s => s.Name == Name);
                    if (site == null)
                        throw new AuctionSiteInvalidOperationException($"{nameof(Name)}: this site not exists");

                    var ses = c.Sessions.Where(s => s.SiteId == site.SiteId && s.ValidUntil > Now())
                        .Select(s => new LSession(s.SessionId.ToString(), s.ValidUntil, 
                            new LUser(s.UserSession.Username, site.SiteId, ConnectionString, AlarmClock), ConnectionString, AlarmClock)).ToList();
                    
                    return ses;
                }
                catch (InvalidOperationException e)
                {
                    throw new AuctionSiteInvalidOperationException(e.Message, e);
                }
                catch (ArgumentNullException e)
                {
                    throw new AuctionSiteArgumentNullException(e.Message, e);
                }
            }
        }

        //Restituisce tutte le aste (non ancora terminate) del sito.
        //onlyNotEnded: Se vero, vengono prese in considerazione solo le aste non ancora terminate.
        public IEnumerable<IAuction> ToyGetAuctions(bool onlyNotEnded)
        {
            using (var c = new ASTapDbContext(ConnectionString))
            {
                if (!c.Database.CanConnect())
                    throw new AuctionSiteUnavailableDbException("BadConnectionString");

                try
                {
                    var site = c.Sites.SingleOrDefault(s => s.Name == Name);
                    if (site == null)
                        throw new AuctionSiteInvalidOperationException($"{nameof(Name)}: this site not exists");

                    var aS = c.Auctions.Where(a => a.Site.Name == Name);

                    if (onlyNotEnded)
                    {
                        var notEndedAuc = aS.Where(a => a.EndsOn > Now())
                            .Select(a => new LAuction(a.AuctionId, a.Description, a.EndsOn, 
                                new LUser(a.Seller.Username, site.SiteId, ConnectionString, AlarmClock), site.SiteId, ConnectionString, AlarmClock));

                        return notEndedAuc.ToList();
                    }
                    else
                    {
                        var allAuc = aS.Select(a => new LAuction(a.AuctionId, a.Description, a.EndsOn , 
                                new LUser(a.Seller.Username, site.SiteId, ConnectionString, AlarmClock), site.SiteId, ConnectionString, AlarmClock));
                        return allAuc.ToList();
                    }
                }
                catch (InvalidOperationException e)
                {
                    throw new AuctionSiteInvalidOperationException(e.Message, e);
                }
                catch (ArgumentNullException e)
                {
                    throw new AuctionSiteArgumentNullException(e.Message, e);
                }
            }

        }

        public ISession? Login(string username, string password)
        {
            UsernameVerify(username);
            PasswordVerify(password);

            using (var c = new ASTapDbContext(ConnectionString))
            {
                if (!c.Database.CanConnect())
                    throw new AuctionSiteUnavailableDbException("BadConnectionString");

                try
                {
                    var site = c.Sites.SingleOrDefault(s => s.Name == Name);
                    if(site == null)
                        throw new AuctionSiteInvalidOperationException($"{nameof(Name)}: this site not exists");

                    var user = c.Users.SingleOrDefault(u => u.Username == username && u.SiteId == site.SiteId);

                    if (user == null)
                        return null;

                    if (user.Password != HashPass(password))
                        return null;

                    var sesD = c.Sessions.SingleOrDefault(s=>s.SiteId == site.SiteId && s.UserId == user.UserId);

                    //restituisco la sessione corrente esistente
                    if (sesD != null)
                    {
                        sesD.ValidUntil = Now().AddSeconds(site.SessionExpirationInSeconds);
                        c.Sessions.Update(sesD);
                        c.SaveChanges();

                        var session = new LSession(sesD.SiteId.ToString(), sesD.ValidUntil, 
                            new LUser(user.Username, site.SiteId, ConnectionString, AlarmClock), ConnectionString, AlarmClock);

                        return session;
                    }
                    else
                    {
                        Session newSession = new Session(Now().AddSeconds(site.SessionExpirationInSeconds), user.UserId, site.SiteId);

                        c.Sessions.Add(newSession);
                        c.SaveChanges();

                        return new LSession(newSession.SessionId.ToString(), newSession.ValidUntil,
                            new LUser(newSession.UserSession.Username, site.SiteId, ConnectionString, AlarmClock), ConnectionString, AlarmClock);
                    }
                }
                catch (InvalidOperationException e)
                {
                    throw new AuctionSiteInvalidOperationException(e.Message, e); //viene l'anciata se il sito non esiste per la query chiamata site
                }
                catch (ArgumentNullException e)
                {
                    throw new AuctionSiteInexistentNameException(e.Message, e.Message, e);
                }
            }
        }

        public void CreateUser(string username, string password)
        {
            UsernameVerify(username);
            PasswordVerify(password);

            using (var c = new ASTapDbContext(ConnectionString))
            {
                if (!c.Database.CanConnect())
                    throw new AuctionSiteUnavailableDbException("BadConnectionString");

                try
                {
                    var userL = c.Users.SingleOrDefault(u => u.Username == username);
                    if(userL != null)
                        throw new AuctionSiteNameAlreadyInUseException($"{nameof(username)} of an existing site");

                    var site = c.Sites.Single(s => s.Name == Name);
                    User user = new User(username, HashPass(password), site.SiteId);

                    c.Users.Add(user);
                    c.SaveChanges();
                }
                catch (InvalidOperationException e)
                {
                    throw new AuctionSiteInvalidOperationException(e.Message, e);
                }
                catch (ArgumentNullException e)
                {
                    throw new AuctionSiteArgumentNullException(e.Message, e);
                }
            }
        }

        public void Delete()
        {
            using (var c = new ASTapDbContext(ConnectionString))
            {
                if (!c.Database.CanConnect())
                    throw new AuctionSiteUnavailableDbException("BadConnectionString");

                try
                {
                    var site = c.Sites.Where(s => s.Name == Name)
                        .Include(s => s.Users).First();

                    c.Sites.Remove(site);
                    c.SaveChanges();
                }
                catch (InvalidOperationException e)
                {
                    throw new AuctionSiteInvalidOperationException(e.Message, e);
                }
                catch (ArgumentNullException e)
                {
                    throw new AuctionSiteArgumentNullException(e.Message, e);
                }
            }
        }

        public DateTime Now()
        {
            return AlarmClock.Now;
        }

        //FUNZIONI INTERNE
        private static void UsernameVerify(string username)
        {
            if (username == null)
                throw new AuctionSiteArgumentNullException($"{nameof(username)} is null");
            if (username == String.Empty)
                throw new AuctionSiteArgumentException($"{nameof(username)} is Empty");
            if (username.Length < DomainConstraints.MinUserName)
                throw new AuctionSiteArgumentException($"{nameof(username)} length too small");
            if (username.Length > DomainConstraints.MaxUserName)
                throw new AuctionSiteArgumentException($"{nameof(username)} length too large");
        }

        private static void PasswordVerify(string password)
        {
            if (password == null)
                throw new AuctionSiteArgumentNullException($"{nameof(password)} is null");
            if (password == String.Empty)
                throw new AuctionSiteArgumentException($"{nameof(password)} is Empty");
            if (password.Length < DomainConstraints.MinUserPassword)
                throw new AuctionSiteArgumentException($"{nameof(password)} length too small");
        }

        private static string HashPass(string password)
        {
            try
            {
                byte[] encData_byte = new byte[password.Length];
                encData_byte = System.Text.Encoding.UTF8.GetBytes(password);
                string encodedData = Convert.ToBase64String(encData_byte);
                
                return encodedData;
            }
            catch (ArgumentNullException e)
            {
                throw new AuctionSiteArgumentNullException(e.Message, e);
            }
            catch (EncoderFallbackException e)
            {
                throw new AuctionSiteInvalidOperationException(e.Message, e);
            }
        }

        public override bool Equals(object? obj)
        {
            if (obj == null)
                return false;

            if (obj.GetType() == typeof(LSite))
            {
                LSite other = (LSite)obj;
                return this.Name == other.Name;
            }

            return false;
        }

        public override int GetHashCode()
        {
            return Name.GetHashCode();
        }
    }

    public class LSession : ISession
    {
        public string Id { get; } 
        public DateTime ValidUntil { get; set; } 
        public IUser User { get; } 

        public string ConnectionString { get; }
        public IAlarmClock AlarmClock { get; }

        public LSession(string id, DateTime validUntil, IUser user, string connectionString, IAlarmClock alarmClock)
        {
            Id = id;
            ValidUntil = validUntil;
            User = user;

            ConnectionString = connectionString;
            AlarmClock = alarmClock;
        }

        public void Logout()
        {
            using (var c = new ASTapDbContext(ConnectionString))
            {
                if (!c.Database.CanConnect())
                    throw new AuctionSiteUnavailableDbException("BadConnectionString");
                try
                {
                    var session = c.Sessions.Single(s => s.SessionId.ToString() == Id);
                    c.Sessions.Remove(session);
                    c.SaveChanges();
                }
                catch (InvalidOperationException e)
                {
                    throw new AuctionSiteInvalidOperationException(e.Message, e);
                }
                catch (ArgumentNullException e)
                {
                    throw new AuctionSiteArgumentNullException(e.Message, e);
                }
            }
        }

        public IAuction CreateAuction(string description, DateTime endsOn, double startingPrice)
        {
            
            if (description == null)
                throw new AuctionSiteArgumentNullException();
            if (description == String.Empty)
                throw new AuctionSiteArgumentException();
            
            if (endsOn < AlarmClock.Now)
                throw new AuctionSiteUnavailableTimeMachineException();
            
            if (startingPrice < 0)
                throw new AuctionSiteArgumentOutOfRangeException();

            if (ValidUntil < AlarmClock.Now)
                throw new AuctionSiteInvalidOperationException($"{nameof(ValidUntil)} expired session");

            //CREAZIONE
            using (var c = new ASTapDbContext(ConnectionString))
            {
                if (!c.Database.CanConnect())
                    throw new AuctionSiteUnavailableDbException("BadConnectionString");

                try
                {
                    var user = c.Users.Single(u => u.Username == User.Username);
                    var site = c.Sites.Single(s => s.SiteId == user.SiteId);

                    //aggiorno il tempo di scadenza della sessione viene reimpostato (allo stesso valore come se la sessione fosse stata appena creata).
                    ValidUntil = AlarmClock.Now.AddSeconds(site.SessionExpirationInSeconds); 

                    var thiSession = c.Sessions.Single(s => s.SessionId.ToString() == Id);
                    thiSession.ValidUntil = ValidUntil; 
                    c.Sessions.Update(thiSession);

                    Auction auction = new Auction(description, startingPrice, endsOn, user.UserId, site.SiteId);
                    c.Auctions.Add(auction);
                    c.SaveChanges();

                    return new LAuction(auction.AuctionId, description, endsOn, User, site.SiteId, ConnectionString, AlarmClock);
                }
                catch (InvalidOperationException e)
                {
                    throw new AuctionSiteInvalidOperationException(e.Message, e);
                }
                catch (ArgumentNullException e)
                {
                    throw new AuctionSiteInexistentNameException(e.Message, e.Message, e);
                }
            }
        }

        public override bool Equals(object? obj)
        {
            if (obj == null)
                return false;

            if (obj.GetType() == typeof(LSession))
            {
                LSession other = (LSession)obj;
                return this.User.Username == other.User.Username;
            }

            return false;
        }

        public override int GetHashCode()
        {
            return User.Username.GetHashCode();
        }
    }

    public class LAuction : IAuction
    {
        public int Id { get; }
        public string Description { get; }
        public DateTime EndsOn { get; } 
        public IUser Seller { get; }

        private double MaximumOffer { get; set; } = 0; 
        public int SiteId { get; }
        public string ConnectionString { get; }
        public IAlarmClock AlarmClock { get; }

        public LAuction(int id, string description, DateTime endsOn, IUser seller, int siteId, string connectionString, IAlarmClock alarmClock)
        {
            Id = id;
            Description = description;
            EndsOn = endsOn;
            Seller = seller;

            SiteId = siteId;
            ConnectionString = connectionString;
            AlarmClock = alarmClock;
        }

        public IUser? CurrentWinner()
        {
            using (var c = new ASTapDbContext(ConnectionString))
            {
                if (!c.Database.CanConnect())
                    throw new AuctionSiteUnavailableDbException("BadConnectionString");

                try
                {
                    var auctionD = c.Auctions.SingleOrDefault(a => a.Seller.Username == Seller.Username && a.SiteId == SiteId);
                    
                    if (auctionD == null)
                        throw new AuctionSiteInexistentNameException($"Auction: {nameof(Id)} is not exist");

                    if (auctionD.EndsOn < AlarmClock.Now)
                        return null;

                    var site = c.Sites.SingleOrDefault(s => s.SiteId == SiteId);
                    if(site == null)
                        throw new AuctionSiteInvalidOperationException($"Site: {nameof(SiteId)}: this site not exists");
                    
                    var user = c.Users.SingleOrDefault(u => u.SiteId == site.SiteId && u.Username == auctionD.WinUser);
                    if (user == null)
                        return null;

                    if (auctionD.WinUser == null)
                        return null;

                    return new LUser(auctionD.WinUser, site.SiteId, ConnectionString, AlarmClock);
                }
                catch (InvalidOperationException e)
                {
                    throw new AuctionSiteInvalidOperationException(e.Message);
                }
                catch (ArgumentNullException e)
                {
                    throw new AuctionSiteInexistentNameException(e.Message);
                }
            }
        }

        public double CurrentPrice()
        {
            using (var c = new ASTapDbContext(ConnectionString))
            {
                if (!c.Database.CanConnect())
                    throw new AuctionSiteUnavailableDbException("BadConnectionString");

                try
                {
                    var auctionD = c.Auctions.SingleOrDefault(a => a.Seller.Username == Seller.Username 
                                                                   && a.SiteId == SiteId);
                    if (auctionD == null)
                        throw new AuctionSiteArgumentOutOfRangeException($"{nameof(EndsOn)} auction expired");

                    return auctionD.BestOffer;
                }
                catch (InvalidOperationException e)
                {
                    throw new AuctionSiteInvalidOperationException(e.Message);
                }
                catch (ArgumentNullException e)
                {
                    throw new AuctionSiteInexistentNameException(e.Message);
                }
            }
        }

        public void Delete()
        {
            using (var c = new ASTapDbContext(ConnectionString))
            {
                if (!c.Database.CanConnect())
                    throw new AuctionSiteUnavailableDbException("BadConnectionString");

                try
                {
                    var action = c.Auctions.SingleOrDefault(a => a.AuctionId == Id);
                    if (action == null)
                        throw new AuctionSiteInvalidOperationException($"{nameof(action)} is null");

                    c.Auctions.Remove(action);
                    c.SaveChanges();
                }
                catch (InvalidOperationException e)
                {
                    throw new AuctionSiteInvalidOperationException(e.Message);
                }
                catch (ArgumentNullException e)
                {
                    throw new AuctionSiteArgumentNullException(e.Message);
                }
            }
        }

        public bool Bid(ISession session, double offer)
        {
            using (var c = new ASTapDbContext(ConnectionString))
            {
                if (!c.Database.CanConnect())
                    throw new AuctionSiteUnavailableDbException("BadConnectionString");

                var aD = c.Auctions.SingleOrDefault(a => a.Seller.Username == Seller.Username && a.SiteId == SiteId && a.AuctionId == Id);
                if (aD == null)
                    throw new AuctionSiteInvalidOperationException($"Auction: {nameof(Id)} is not exist");

                var auctionD = c.Auctions.SingleOrDefault(a => a.AuctionId == aD.AuctionId && a.EndsOn > AlarmClock.Now);  
                
                if (auctionD == null) 
                    throw new AuctionSiteInvalidOperationException($"{nameof(EndsOn)} auction expired");

                var site = c.Sites.SingleOrDefault(s => s.SiteId == SiteId);
                
                if (site == null)
                    throw new AuctionSiteInvalidOperationException($"Site: {nameof(SiteId)}: this site not exists");

                if (offer < 0)
                    throw new AuctionSiteArgumentOutOfRangeException($"{nameof(offer)} is negative");

                if(session == null)
                    throw new AuctionSiteArgumentNullException($"{nameof(session)} is null");

                var sessionD = c.Sessions.SingleOrDefault(s => s.SessionId.ToString() == session.Id);
                
                if (sessionD == null)
                    throw new AuctionSiteArgumentException($"{nameof(sessionD)} is null");

                if (sessionD.ValidUntil < AlarmClock.Now)
                    throw new AuctionSiteArgumentException($"{nameof(sessionD)} is not valid anymore");

                var sesL = session as LSession;
                if (sesL != null) 
                    sesL.ValidUntil = AlarmClock.Now.AddSeconds(site.SessionExpirationInSeconds);

                var user = c.Users.SingleOrDefault(u => u.SessionUser.SessionId.ToString() == session.Id );
                if (user == null)
                    throw new AuctionSiteInvalidOperationException($"{nameof(user)} is not exist");

                if (user.Username == Seller.Username)
                    throw new AuctionSiteArgumentException($"the logged user is also the Seller of this auction");

                var seller = c.Users.SingleOrDefault(u => u.Username == Seller.Username);
                if (seller == null)
                    throw new AuctionSiteInvalidOperationException($"{nameof(seller)} is not exist");
                
                if (user.SiteId != seller.SiteId)
                    throw new AuctionSiteArgumentException($"the logged user is a user of a site different from the site of the Seller");


                var usernameSession = session.User.Username;
                
                if (usernameSession == auctionD.WinUser && offer < MaximumOffer + site.MinimumBidIncrement)
                    return false;

                if (usernameSession != auctionD.WinUser && offer < auctionD.BestOffer)
                    return false;

                if (usernameSession != auctionD.WinUser && offer < auctionD.BestOffer + site.MinimumBidIncrement && auctionD.WinUser != null)
                    return false;

                try
                {
                    if (auctionD.WinUser == null)
                    {
                        MaximumOffer = offer;
                        auctionD.WinUser = usernameSession;

                        c.Auctions.Update(auctionD);
                        c.SaveChanges();
                        return true;
                    }

                    if (auctionD.WinUser == usernameSession)
                    {
                        MaximumOffer = offer;
                        return true;
                    }

                    if (auctionD.WinUser != null && usernameSession != auctionD.WinUser && offer > MaximumOffer)
                    {
                        if (offer < MaximumOffer + site.MinimumBidIncrement)
                            auctionD.BestOffer = offer;
                        else
                            auctionD.BestOffer = MaximumOffer + site.MinimumBidIncrement;

                        MaximumOffer = offer; 
                        auctionD.WinUser = usernameSession; 

                        c.Auctions.Update(auctionD);
                        c.SaveChanges();
                        return true;
                    }

                    if (auctionD.WinUser != null && usernameSession != auctionD.WinUser && offer < MaximumOffer)
                    {
                        if (MaximumOffer < offer + site.MinimumBidIncrement)
                            auctionD.BestOffer = MaximumOffer;
                        else
                            auctionD.BestOffer = offer + site.MinimumBidIncrement;

                        c.Auctions.Update(auctionD);
                        c.SaveChanges();
                        return true;
                    }

                }
                catch (InvalidOperationException e)
                {
                    throw new AuctionSiteInvalidOperationException(e.Message, e);
                }
                catch (ArgumentNullException e)
                {
                    throw new AuctionSiteInexistentNameException(e.Message, e.Message, e);
                }

                return true;
            }
        }

        public override bool Equals(object? obj)
        {
            if (obj == null)
                return false;

            if (obj.GetType() == typeof(LAuction))
            {
                LAuction other = (LAuction)obj;
                return this.Id == other.Id;
            }

            return false;
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }
    }

    public class LUser : IUser
    {
        public string Username { get; }

        public int SiteId { get; set; }
        public string ConnectionString { get; }
        public IAlarmClock AlarmClock { get; }

        public LUser(string username, int siteId, string connectionString, IAlarmClock alarmClock)
        {
            Username = username;

            SiteId = siteId;
            ConnectionString = connectionString;
            AlarmClock = alarmClock;
        }

        public IEnumerable<IAuction> WonAuctions()
        {
            using (var c = new ASTapDbContext(ConnectionString))
            {
                if (!c.Database.CanConnect())
                    throw new AuctionSiteUnavailableDbException("BadConnectionString");

                try
                {
                    var auction = c.Auctions
                        .Where(a => a.WinUser == Username && a.SiteId == SiteId && a.EndsOn < AlarmClock.Now)
                        .Select(a => new LAuction(a.AuctionId, a.Description, a.EndsOn, 
                            new LUser(a.Seller.Username, a.Seller.SiteId, ConnectionString, AlarmClock),
                            a.SiteId, ConnectionString, AlarmClock));

                    return auction.ToList();
                }
                catch (ArgumentNullException e)
                {
                    throw new AuctionSiteArgumentNullException(e.Message, e);
                }
            }
        }

        public void Delete()
        {
            using (var c = new ASTapDbContext(ConnectionString))
            {
                if (!c.Database.CanConnect())
                    throw new AuctionSiteUnavailableDbException("BadConnectionString");

                try
                {
                    var user = c.Users.Single(u => u.Username == Username && u.SiteId == SiteId);
                    
                    var auctionProp = c.Auctions.SingleOrDefault(a => a.Seller.UserId == user.UserId && a.EndsOn > AlarmClock.Now && a.SiteId == SiteId);
                    if (auctionProp != null)
                        throw new AuctionSiteInvalidOperationException();

                    
                    var auctionWin = c.Auctions.SingleOrDefault(a => a.WinUser == user.Username && a.EndsOn > AlarmClock.Now && a.SiteId == SiteId);
                    if (auctionWin != null)
                        throw new AuctionSiteInvalidOperationException();

                    c.Users.Remove(user);
                    c.SaveChanges();
                }
                catch (InvalidOperationException e)
                {
                    throw new AuctionSiteInvalidOperationException(e.Message, e);
                }
                catch (ArgumentNullException e)
                {
                    throw new AuctionSiteArgumentNullException(e.Message, e);
                }
            }
        }

        public override bool Equals(object? obj)
        {
            if (obj == null)
                return false;

            if (obj.GetType() == typeof(LUser))
            {
                LUser other = (LUser) obj;
                return this.Username == other.Username;
            }

            return false;
        }

        public override int GetHashCode()
        {
            return Username.GetHashCode();
        }
    }
}
