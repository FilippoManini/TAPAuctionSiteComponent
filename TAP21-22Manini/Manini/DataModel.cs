using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TAP21_22_AuctionSite.Interface;

namespace Manini.Data
{
    public class ASTapDbContext : TapDbContext
    {
        public DbSet<User> Users { get; set; }
        public DbSet<Auction> Auctions { get; set; }
        public DbSet<Session> Sessions { get; set; }
        public DbSet<Site> Sites { get; set; }
        
        public ASTapDbContext(string connectionString) : base(new DbContextOptionsBuilder<ASTapDbContext>().UseSqlServer(connectionString).Options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var u = modelBuilder.Entity<User>();
            u.HasOne(u => u.SiteUser).
                WithMany(s => s.Users).
                OnDelete(DeleteBehavior.ClientCascade);
        }

        public override int SaveChanges()
        {
            try
            {
                return base.SaveChanges();
            }
            catch (SqlException e)
            {
                throw new AuctionSiteUnavailableDbException("Unavailable Db", e);
            }
            catch (DbUpdateException e)
            {
                var sqlException = e.InnerException as SqlException;

                if (null == sqlException) 
                    throw new AuctionSiteArgumentNullException($"{sqlException} is null", e);

                switch (sqlException.Number)
                {
                    case 2601: throw new AuctionSiteUnavailableDbException("Cannot insert duplicate key row in object", e);
                    
                    default:
                        throw new AuctionSiteUnavailableDbException(e.Message, e);
                }
            }
        }
    }


    
    [Index(nameof(Username), IsUnique = true, Name = "UsernameUnique")]
    public class User
    {
        //CAMPI
        public int UserId { get; set; } //key

        [MinLength(DomainConstraints.MinUserName)]
        [MaxLength(DomainConstraints.MaxUserName)]
        public string Username { get; set; } //Lo stesso nome utente può essere utilizzato da utenti diversi su siti diversi.
        public string Password { get; set; }

        //PROPRIETA DI NAVIGAZIONE
        public Site SiteUser { get; set; }
        public int SiteId { get; set; }
        public Session SessionUser { get; set; }
        public List<Auction> AuctionsUser { get; set; }

        public User(string username, string password, int siteId)
        {
            Username = username;
            Password = password;

            SiteId = siteId;
            AuctionsUser = new List<Auction>();
        }
    }

    //Le aste gestite dai siti. sono uguali se le due aste hanno stesso sito e stesso id
    public class Auction
    {
        //CAMPI
        public int AuctionId { get; set; } //Ottiene la chiave univoca utilizzata per identificare le aste.
        public string Description { get; set; } //Ottiene la descrizione dell'oggetto/servizio offerto
        public DateTime EndsOn { get; set; } //Ottiene il tempo di scadenza dell'asta; nessuna offerta sarà accettata dopo di essa.
        public double BestOffer { get; set; } = 0; //la migliore offerta espressa in double 
        public string? WinUser { get; set; } //l'utente che al momento sta vincendo strettamente collegato a BestOffer;

        //PROPRIETA DI NAVIGAZIONE
        public Site Site { get; set; }
        public int SiteId { get; set; }
        public User Seller { get; set; } //venditore: Ottiene l'utente che vende l'oggetto/servizio. 
        public int UserId { get; set; }
        
        public Auction(string description, double bestOffer, DateTime endsOn, int userId, int siteId)
        {
            Description = description;
            EndsOn = endsOn;
            BestOffer = bestOffer;

            UserId = userId;
            SiteId = siteId;
        }
    }

    public class Session
    {
        //CAMPI
        public int SessionId { get; set; } //key
        public DateTime ValidUntil { get; set; } //Ottiene l'ora di scadenza corrente della sessione.

        //PROPRIETA DI NAVIGAZIONE
        public User UserSession { get; set; } //Ottiene l'utente proprietario della sessione.
        public int UserId { get; set; }
        public Site SiteSession { get; set; }
        public int SiteId { get; set; }

        
        public Session(DateTime validUntil, int userId, int siteId)
        {
            ValidUntil = validUntil;

            //foreign key
            UserId = userId;
            SiteId = siteId;
        }
    }

    [Index(nameof(Name), IsUnique = true, Name = "NameUnique")]
    public class Site 
    {
        //CAMPI
        public int SiteId { get; set; }

        [MinLength(DomainConstraints.MinSiteName)]
        [MaxLength(DomainConstraints.MaxSiteName)]
        public string Name { get; set; } //Il nome del sito dell'asta.

        [MinLength(DomainConstraints.MinTimeZone)]
        [MaxLength(DomainConstraints.MaxTimeZone)]
        public int Timezone { get; set; } //Il fuso orario del sito dell'asta.

        [MinLength(0)] //per essere positivo
        public int SessionExpirationInSeconds { get; set; } //Il numero di secondi necessari per il timeout della sessione di un utente inattivo. Un numero positivo
        [MinLength(0)] //per essere positivo
        public double MinimumBidIncrement { get; set; } //L'importo minimo consentito come incremento (dal prezzo di partenza) per un'offerta. Un numero positivo

        //PROPRIETA DI NAVIGAZIONE
        public List<Session> Sessions { get; set; }
        public List<User> Users { get; set; }
        public List<Auction> Auctions { get; set; }

        public Site(string name, int timezone, int sessionExpirationInSeconds, double minimumBidIncrement)
        {
            Name = name;
            Timezone = timezone;
            SessionExpirationInSeconds = sessionExpirationInSeconds;
            MinimumBidIncrement = minimumBidIncrement;

            Sessions = new List<Session>();
            Auctions = new List<Auction>();
        }
    }
}

