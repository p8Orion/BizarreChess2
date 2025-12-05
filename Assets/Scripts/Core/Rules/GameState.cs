using System;
using System.Collections.Generic;
using System.Linq;
using BizarreChess.Core.Graph;
using BizarreChess.Core.Units;
using BizarreChess.Core.Armies;

namespace BizarreChess.Core.Rules
{
    /// <summary>
    /// Complete state of a game match.
    /// </summary>
    [Serializable]
    public class GameState
    {
        // Match info
        public string MatchId;
        public GamePhase Phase;
        public int TurnNumber;
        public int CurrentPlayerId;

        // Board state
        public BoardState BoardState;

        // Units
        public List<UnitState> Units;
        private int _nextUnitId;

        // Players
        public List<PlayerState> Players;

        // Win condition
        public int? WinnerId;
        public GameEndReason EndReason;

        // History (for undo/replay)
        public List<GameAction> ActionHistory;

        public GameState()
        {
            Units = new List<UnitState>();
            Players = new List<PlayerState>();
            ActionHistory = new List<GameAction>();
            Phase = GamePhase.Setup;
            TurnNumber = 0;
            _nextUnitId = 0;
        }

        #region Initialization

        /// <summary>
        /// Initialize a new game with the given board and armies.
        /// </summary>
        public void Initialize(
            BoardDefinition boardDef,
            List<PlayerSetup> playerSetups)
        {
            MatchId = Guid.NewGuid().ToString();
            BoardState = boardDef.CreateInitialState();
            Phase = GamePhase.Setup;
            TurnNumber = 0;

            // Create players
            for (int i = 0; i < playerSetups.Count; i++)
            {
                var setup = playerSetups[i];
                Players.Add(new PlayerState
                {
                    PlayerId = i,
                    DisplayName = setup.DisplayName,
                    IsReady = false
                });

                // Place army
                var spawnZone = boardDef.SpawnZones[i];
                var placedUnits = ArmyPlacer.PlaceArmy(
                    setup.Army,
                    spawnZone,
                    i,
                    _nextUnitId,
                    i == 1 // Mirror for second player
                );

                _nextUnitId += placedUnits.Count;
                Units.AddRange(placedUnits);
            }

            // Start game
            Phase = GamePhase.Playing;
            CurrentPlayerId = 0; // White moves first
            TurnNumber = 1;
        }

        #endregion

        #region Unit Queries

        public UnitState GetUnit(int unitId)
        {
            return Units.FirstOrDefault(u => u.UnitId == unitId);
        }

        public UnitState GetUnitAtNode(int nodeId)
        {
            return Units.FirstOrDefault(u => u.IsAlive && u.CurrentNodeId == nodeId);
        }

        public List<UnitState> GetPlayerUnits(int playerId)
        {
            return Units.Where(u => u.IsAlive && u.OwnerId == playerId).ToList();
        }

        public List<UnitState> GetAliveUnits()
        {
            return Units.Where(u => u.IsAlive).ToList();
        }

        public bool IsNodeOccupied(int nodeId)
        {
            return Units.Any(u => u.IsAlive && u.CurrentNodeId == nodeId);
        }

        #endregion

        #region Actions

        /// <summary>
        /// Execute a move action.
        /// </summary>
        public void ExecuteMove(int unitId, int targetNode, bool isCapture = false, int? capturedUnitId = null)
        {
            var unit = GetUnit(unitId);
            if (unit == null) return;

            int fromNode = unit.CurrentNodeId;

            // Handle capture
            if (isCapture && capturedUnitId.HasValue)
            {
                var captured = GetUnit(capturedUnitId.Value);
                if (captured != null)
                {
                    captured.IsAlive = false;
                    // Award experience
                    unit.AddExperience(50);
                }
            }

            // Move unit
            unit.MoveTo(targetNode);

            // Record action
            ActionHistory.Add(new GameAction
            {
                Type = ActionType.Move,
                UnitId = unitId,
                FromNode = fromNode,
                ToNode = targetNode,
                CapturedUnitId = capturedUnitId,
                TurnNumber = TurnNumber,
                PlayerId = CurrentPlayerId
            });

            // Handle special tiles
            HandleNodeEffect(unit, targetNode);
        }

        /// <summary>
        /// Execute an attack action (for games with separate attack).
        /// </summary>
        public void ExecuteAttack(int attackerId, int targetId)
        {
            var attacker = GetUnit(attackerId);
            var target = GetUnit(targetId);
            if (attacker == null || target == null) return;

            // Calculate damage
            int damage = attacker.Attack;
            target.TakeDamage(damage);

            attacker.HasActedThisTurn = true;

            if (!target.IsAlive)
            {
                attacker.AddExperience(50);
            }

            // Record action
            ActionHistory.Add(new GameAction
            {
                Type = ActionType.Attack,
                UnitId = attackerId,
                TargetUnitId = targetId,
                Damage = damage,
                TurnNumber = TurnNumber,
                PlayerId = CurrentPlayerId
            });
        }

        private void HandleNodeEffect(UnitState unit, int nodeId)
        {
            var node = BoardState.GetNode(nodeId);

            switch (node.CurrentType)
            {
                case NodeType.Boost:
                    unit.AddModifier(Modifier.CreateBuff("Attack", 2, 3, "BoostTile"));
                    break;

                case NodeType.Trap:
                    unit.TakeDamage(10);
                    break;

                case NodeType.Teleport:
                    if (node.TeleportTargetId >= 0 && BoardState.IsNodePassable(node.TeleportTargetId))
                    {
                        unit.CurrentNodeId = node.TeleportTargetId;
                    }
                    break;

                case NodeType.Unstable:
                    // Nothing immediate, but node might collapse
                    break;
            }
        }

        #endregion

        #region Turn Management

        /// <summary>
        /// End the current player's turn.
        /// </summary>
        public void EndTurn()
        {
            // Process unit end-of-turn
            foreach (var unit in GetPlayerUnits(CurrentPlayerId))
            {
                unit.EndTurn();
            }

            // Process board end-of-turn
            BoardState.ProcessTurnEnd();

            // Switch to next player
            CurrentPlayerId = (CurrentPlayerId + 1) % Players.Count;

            // If back to player 0, increment turn
            if (CurrentPlayerId == 0)
            {
                TurnNumber++;
            }

            // Start new turn for current player
            foreach (var unit in GetPlayerUnits(CurrentPlayerId))
            {
                unit.StartTurn();
            }
        }

        #endregion

        #region Win Conditions

        /// <summary>
        /// Check win conditions and update game state.
        /// </summary>
        public void CheckWinConditions(MoveValidator validator)
        {
            foreach (var player in Players)
            {
                // Check if player has lost their king
                var king = Units.FirstOrDefault(u =>
                    u.IsAlive &&
                    u.OwnerId == player.PlayerId &&
                    u.DefinitionId == "King");

                if (king == null)
                {
                    // Player lost their king
                    int winnerId = Players.First(p => p.PlayerId != player.PlayerId).PlayerId;
                    SetWinner(winnerId, GameEndReason.KingCaptured);
                    return;
                }

                // Check checkmate
                if (validator.IsCheckmate(player.PlayerId, Units))
                {
                    int winnerId = Players.First(p => p.PlayerId != player.PlayerId).PlayerId;
                    SetWinner(winnerId, GameEndReason.Checkmate);
                    return;
                }
            }

            // Check stalemate
            if (validator.IsStalemate(CurrentPlayerId, Units))
            {
                SetDraw(GameEndReason.Stalemate);
            }
        }

        private void SetWinner(int winnerId, GameEndReason reason)
        {
            WinnerId = winnerId;
            EndReason = reason;
            Phase = GamePhase.Ended;
        }

        private void SetDraw(GameEndReason reason)
        {
            WinnerId = null;
            EndReason = reason;
            Phase = GamePhase.Ended;
        }

        #endregion
    }

    #region Supporting Types

    public enum GamePhase
    {
        Setup,
        Placement,  // Manual placement phase
        Playing,
        Ended
    }

    public enum GameEndReason
    {
        None,
        Checkmate,
        KingCaptured,
        Stalemate,
        Resignation,
        Timeout,
        Disconnect
    }

    [Serializable]
    public class PlayerState
    {
        public int PlayerId;
        public string DisplayName;
        public bool IsReady;
        public bool IsConnected;
    }

    public class PlayerSetup
    {
        public string DisplayName;
        public ArmyDefinition Army;
    }

    [Serializable]
    public class GameAction
    {
        public ActionType Type;
        public int TurnNumber;
        public int PlayerId;
        public int UnitId;
        public int? FromNode;
        public int? ToNode;
        public int? TargetUnitId;
        public int? CapturedUnitId;
        public int? Damage;
        public string AbilityId;
    }

    public enum ActionType
    {
        Move,
        Attack,
        Ability,
        Spawn,
        EndTurn
    }

    #endregion
}

