using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace BizarreChess.Persistence
{
    /// <summary>
    /// Mock implementation of IProfileService for local development.
    /// Stores data in PlayerPrefs (not for production!).
    /// </summary>
    public class MockProfileService : IProfileService
    {
        private const string PROFILE_KEY = "BizarreChess_MockProfile";
        private const string UNITS_KEY = "BizarreChess_MockUnits";
        private const string ARMIES_KEY = "BizarreChess_MockArmies";

        private PlayerProfile _cachedProfile;
        private List<OwnedUnit> _cachedUnits;
        private List<SavedArmy> _cachedArmies;
        private string _playerId;

        public bool IsAuthenticated => !string.IsNullOrEmpty(_playerId);

        #region Authentication

        public Task<AuthResult> Authenticate()
        {
            // Generate or retrieve a mock player ID
            _playerId = PlayerPrefs.GetString("BizarreChess_MockPlayerId", "");
            bool isNew = false;

            if (string.IsNullOrEmpty(_playerId))
            {
                _playerId = Guid.NewGuid().ToString();
                PlayerPrefs.SetString("BizarreChess_MockPlayerId", _playerId);
                isNew = true;

                // Create default profile
                CreateDefaultProfile();
            }

            LoadFromStorage();

            Debug.Log($"[MockProfileService] Authenticated as {_playerId} (new: {isNew})");
            return Task.FromResult(AuthResult.Succeeded(_playerId, isNew));
        }

        public Task<string> GetPlayerId()
        {
            return Task.FromResult(_playerId);
        }

        public Task SignOut()
        {
            _playerId = null;
            _cachedProfile = null;
            _cachedUnits = null;
            _cachedArmies = null;
            return Task.CompletedTask;
        }

        #endregion

        #region Profile

        public Task<PlayerProfile> GetProfile()
        {
            if (_cachedProfile == null)
                LoadFromStorage();

            return Task.FromResult(_cachedProfile);
        }

        public Task SaveProfile(PlayerProfile profile)
        {
            _cachedProfile = profile;
            SaveToStorage();
            return Task.CompletedTask;
        }

        private void CreateDefaultProfile()
        {
            _cachedProfile = new PlayerProfile
            {
                PlayerId = _playerId,
                DisplayName = $"Player_{_playerId.Substring(0, 6)}",
                PlayerLevel = 1,
                Experience = 0,
                Currency = 1000,
                GamesPlayed = 0,
                Wins = 0,
                Losses = 0,
                RankPoints = 1000
            };

            // Create default units (classic chess army)
            _cachedUnits = new List<OwnedUnit>();
            CreateDefaultArmy();

            _cachedArmies = new List<SavedArmy>();
            CreateDefaultSavedArmy();

            SaveToStorage();
        }

        private void CreateDefaultArmy()
        {
            // Create one of each piece type
            var pieceTypes = new[] { "King", "Queen", "Rook", "Rook", "Bishop", "Bishop", "Knight", "Knight" };
            
            foreach (var type in pieceTypes)
            {
                _cachedUnits.Add(new OwnedUnit
                {
                    OwnedUnitId = Guid.NewGuid().ToString(),
                    DefinitionId = type,
                    Level = 1,
                    TotalExperience = 0
                });
            }

            // 8 pawns
            for (int i = 0; i < 8; i++)
            {
                _cachedUnits.Add(new OwnedUnit
                {
                    OwnedUnitId = Guid.NewGuid().ToString(),
                    DefinitionId = "Pawn",
                    Level = 1,
                    TotalExperience = 0
                });
            }
        }

        private void CreateDefaultSavedArmy()
        {
            var army = new SavedArmy
            {
                ArmyId = Guid.NewGuid().ToString(),
                Name = "Classic Army",
                Slots = new List<ArmySlotBinding>()
            };

            // Bind units to slots in order
            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                army.Slots.Add(new ArmySlotBinding
                {
                    SlotIndex = i,
                    OwnedUnitId = _cachedUnits[i].OwnedUnitId
                });
            }

            _cachedArmies.Add(army);
        }

        #endregion

        #region Units

        public Task<List<OwnedUnit>> GetOwnedUnits()
        {
            if (_cachedUnits == null)
                LoadFromStorage();

            return Task.FromResult(_cachedUnits.ToList());
        }

        public Task<OwnedUnit> AddUnit(string definitionId)
        {
            var unit = new OwnedUnit
            {
                OwnedUnitId = Guid.NewGuid().ToString(),
                DefinitionId = definitionId,
                Level = 1,
                TotalExperience = 0,
                AcquiredDate = DateTime.UtcNow
            };

            _cachedUnits.Add(unit);
            SaveToStorage();

            return Task.FromResult(unit);
        }

        public Task UpdateUnit(OwnedUnit unit)
        {
            var index = _cachedUnits.FindIndex(u => u.OwnedUnitId == unit.OwnedUnitId);
            if (index >= 0)
            {
                _cachedUnits[index] = unit;
                SaveToStorage();
            }
            return Task.CompletedTask;
        }

        public Task RemoveUnit(string ownedUnitId)
        {
            _cachedUnits.RemoveAll(u => u.OwnedUnitId == ownedUnitId);
            SaveToStorage();
            return Task.CompletedTask;
        }

        #endregion

        #region Armies

        public Task<List<SavedArmy>> GetSavedArmies()
        {
            if (_cachedArmies == null)
                LoadFromStorage();

            return Task.FromResult(_cachedArmies.ToList());
        }

        public Task SaveArmy(SavedArmy army)
        {
            var index = _cachedArmies.FindIndex(a => a.ArmyId == army.ArmyId);
            if (index >= 0)
            {
                _cachedArmies[index] = army;
            }
            else
            {
                if (string.IsNullOrEmpty(army.ArmyId))
                    army.ArmyId = Guid.NewGuid().ToString();
                _cachedArmies.Add(army);
            }
            SaveToStorage();
            return Task.CompletedTask;
        }

        public Task DeleteArmy(string armyId)
        {
            _cachedArmies.RemoveAll(a => a.ArmyId == armyId);
            SaveToStorage();
            return Task.CompletedTask;
        }

        #endregion

        #region Match Results

        public Task ReportMatchResult(MatchResult result)
        {
            // Update profile stats
            _cachedProfile.GamesPlayed++;
            if (result.Won)
                _cachedProfile.Wins++;
            else if (result.IsDraw)
                _cachedProfile.Draws++;
            else
                _cachedProfile.Losses++;

            // Update rank points (simple Elo-like)
            if (result.Won)
                _cachedProfile.RankPoints += 25;
            else if (!result.IsDraw)
                _cachedProfile.RankPoints = Math.Max(0, _cachedProfile.RankPoints - 20);

            // Update unit stats
            foreach (var unitResult in result.UnitResults)
            {
                var unit = _cachedUnits.FirstOrDefault(u => u.OwnedUnitId == unitResult.OwnedUnitId);
                if (unit != null)
                {
                    unit.TotalExperience += unitResult.ExperienceGained;
                    unit.TotalKills += unitResult.Kills;
                    if (!unitResult.Survived)
                        unit.TotalDeaths++;
                    unit.GamesPlayedWith++;

                    // Level up check (simple formula)
                    int newLevel = 1 + unit.TotalExperience / 100;
                    if (newLevel > unit.Level)
                    {
                        unit.Level = newLevel;
                        Debug.Log($"[MockProfileService] Unit {unit.DefinitionId} leveled up to {newLevel}!");
                    }
                }
            }

            SaveToStorage();
            return Task.CompletedTask;
        }

        #endregion

        #region Storage

        private void LoadFromStorage()
        {
            try
            {
                var profileJson = PlayerPrefs.GetString(PROFILE_KEY, "");
                var unitsJson = PlayerPrefs.GetString(UNITS_KEY, "");
                var armiesJson = PlayerPrefs.GetString(ARMIES_KEY, "");

                if (!string.IsNullOrEmpty(profileJson))
                    _cachedProfile = JsonUtility.FromJson<PlayerProfile>(profileJson);
                
                if (!string.IsNullOrEmpty(unitsJson))
                    _cachedUnits = JsonUtility.FromJson<OwnedUnitList>(unitsJson).Units;
                
                if (!string.IsNullOrEmpty(armiesJson))
                    _cachedArmies = JsonUtility.FromJson<SavedArmyList>(armiesJson).Armies;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MockProfileService] Failed to load data: {e.Message}");
            }

            // Ensure lists are initialized
            _cachedUnits ??= new List<OwnedUnit>();
            _cachedArmies ??= new List<SavedArmy>();
        }

        private void SaveToStorage()
        {
            try
            {
                PlayerPrefs.SetString(PROFILE_KEY, JsonUtility.ToJson(_cachedProfile));
                PlayerPrefs.SetString(UNITS_KEY, JsonUtility.ToJson(new OwnedUnitList { Units = _cachedUnits }));
                PlayerPrefs.SetString(ARMIES_KEY, JsonUtility.ToJson(new SavedArmyList { Armies = _cachedArmies }));
                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                Debug.LogError($"[MockProfileService] Failed to save data: {e.Message}");
            }
        }

        /// <summary>
        /// Clear all mock data (for testing).
        /// </summary>
        public void ClearAllData()
        {
            PlayerPrefs.DeleteKey(PROFILE_KEY);
            PlayerPrefs.DeleteKey(UNITS_KEY);
            PlayerPrefs.DeleteKey(ARMIES_KEY);
            PlayerPrefs.DeleteKey("BizarreChess_MockPlayerId");
            PlayerPrefs.Save();

            _cachedProfile = null;
            _cachedUnits = null;
            _cachedArmies = null;
            _playerId = null;
        }

        #endregion

        // Helper classes for JSON serialization (Unity's JsonUtility doesn't support List<T> at root)
        [Serializable]
        private class OwnedUnitList
        {
            public List<OwnedUnit> Units;
        }

        [Serializable]
        private class SavedArmyList
        {
            public List<SavedArmy> Armies;
        }
    }
}

