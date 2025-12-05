using System.Collections.Generic;
using System.Threading.Tasks;

namespace BizarreChess.Persistence
{
    /// <summary>
    /// Interface for profile/persistence services.
    /// Implementations can be: Mock (local), Unity Gaming Services, PlayFab, Firebase, custom REST, etc.
    /// This is SEPARATE from the game server - this handles player profiles and progression.
    /// </summary>
    public interface IProfileService
    {
        #region Authentication

        /// <summary>
        /// Authenticate the player (login or create account).
        /// </summary>
        Task<AuthResult> Authenticate();

        /// <summary>
        /// Get the current player's ID.
        /// </summary>
        Task<string> GetPlayerId();

        /// <summary>
        /// Check if the player is authenticated.
        /// </summary>
        bool IsAuthenticated { get; }

        /// <summary>
        /// Sign out the current player.
        /// </summary>
        Task SignOut();

        #endregion

        #region Profile

        /// <summary>
        /// Get the current player's profile.
        /// </summary>
        Task<PlayerProfile> GetProfile();

        /// <summary>
        /// Save/update the player's profile.
        /// </summary>
        Task SaveProfile(PlayerProfile profile);

        #endregion

        #region Units (Collection)

        /// <summary>
        /// Get all units owned by the player.
        /// </summary>
        Task<List<OwnedUnit>> GetOwnedUnits();

        /// <summary>
        /// Add a new unit to the player's collection.
        /// </summary>
        Task<OwnedUnit> AddUnit(string definitionId);

        /// <summary>
        /// Update an existing unit (after match, level up, etc.).
        /// </summary>
        Task UpdateUnit(OwnedUnit unit);

        /// <summary>
        /// Remove a unit from the collection.
        /// </summary>
        Task RemoveUnit(string ownedUnitId);

        #endregion

        #region Armies

        /// <summary>
        /// Get all saved armies.
        /// </summary>
        Task<List<SavedArmy>> GetSavedArmies();

        /// <summary>
        /// Save/update an army.
        /// </summary>
        Task SaveArmy(SavedArmy army);

        /// <summary>
        /// Delete a saved army.
        /// </summary>
        Task DeleteArmy(string armyId);

        #endregion

        #region Match Results

        /// <summary>
        /// Report match results to update stats and progression.
        /// </summary>
        Task ReportMatchResult(MatchResult result);

        #endregion
    }

    #region Data Models

    /// <summary>
    /// Result of authentication attempt.
    /// </summary>
    public class AuthResult
    {
        public bool Success;
        public string PlayerId;
        public string Error;
        public bool IsNewPlayer;

        public static AuthResult Succeeded(string playerId, bool isNew = false) => new AuthResult
        {
            Success = true,
            PlayerId = playerId,
            IsNewPlayer = isNew
        };

        public static AuthResult Failed(string error) => new AuthResult
        {
            Success = false,
            Error = error
        };
    }

    /// <summary>
    /// Player profile data (persistent).
    /// </summary>
    public class PlayerProfile
    {
        public string PlayerId;
        public string DisplayName;
        public int PlayerLevel;
        public int Experience;
        public int Currency;

        // Stats
        public int GamesPlayed;
        public int Wins;
        public int Losses;
        public int Draws;
        public int RankPoints;

        // Cosmetics
        public PlayerCosmetics Cosmetics;

        public PlayerProfile()
        {
            Cosmetics = new PlayerCosmetics();
        }
    }

    /// <summary>
    /// Player cosmetic choices.
    /// </summary>
    public class PlayerCosmetics
    {
        public string PrimaryColorHex = "#FFFFFF";
        public string SecondaryColorHex = "#000000";
        public string SelectedBoardSkin;
        public string SelectedPieceSkin;
    }

    /// <summary>
    /// A unit owned by the player (persistent progress).
    /// </summary>
    public class OwnedUnit
    {
        public string OwnedUnitId;       // Unique instance ID
        public string DefinitionId;      // Type of unit
        
        // Progression (PERSISTENT between matches)
        public int Level;
        public int TotalExperience;
        
        // Equipment (PERSISTENT)
        public List<string> PermanentEquipment;
        
        // Unlocks
        public List<string> UnlockedAbilities;
        
        // Customization
        public string SkinId;
        
        // Stats
        public System.DateTime AcquiredDate;
        public int GamesPlayedWith;
        public int TotalKills;
        public int TotalDeaths;

        public OwnedUnit()
        {
            PermanentEquipment = new List<string>();
            UnlockedAbilities = new List<string>();
            AcquiredDate = System.DateTime.UtcNow;
            Level = 1;
        }
    }

    /// <summary>
    /// A saved army configuration.
    /// </summary>
    public class SavedArmy
    {
        public string ArmyId;
        public string Name;
        public List<ArmySlotBinding> Slots;

        public SavedArmy()
        {
            Slots = new List<ArmySlotBinding>();
        }
    }

    /// <summary>
    /// Binding between army slot and owned unit.
    /// </summary>
    public class ArmySlotBinding
    {
        public int SlotIndex;
        public string OwnedUnitId;
    }

    /// <summary>
    /// Match result data for reporting.
    /// </summary>
    public class MatchResult
    {
        public string MatchId;
        public bool Won;
        public bool IsDraw;
        public int TurnsPlayed;
        public List<UnitMatchResult> UnitResults;

        public MatchResult()
        {
            UnitResults = new List<UnitMatchResult>();
        }
    }

    /// <summary>
    /// Individual unit performance in a match.
    /// </summary>
    public class UnitMatchResult
    {
        public string OwnedUnitId;
        public int ExperienceGained;
        public int Kills;
        public bool Survived;
        public int DamageDealt;
        public int DamageTaken;
    }

    #endregion
}

