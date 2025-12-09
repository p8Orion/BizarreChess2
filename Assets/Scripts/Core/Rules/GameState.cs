using System;
using System.Collections.Generic;
using System.Linq;
using BizarreChess.Core.Board;
using BizarreChess.Core.Units;
using BizarreChess.Core.Armies;
using BizarreChess.Core.Skills;

namespace BizarreChess.Core.Rules
{
    /// <summary>
    /// Result of executing a full move (validate + execute + check win + end turn).
    /// Contains all info needed for visual/network callbacks.
    /// </summary>
    public class MoveExecutionResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        
        // Move info
        public int UnitId { get; set; }
        public int FromNode { get; set; }
        public int ToNode { get; set; }
        
        // Capture info
        public bool IsCapture { get; set; }
        public int? CapturedUnitId { get; set; }
        public bool IsRangedCapture { get; set; }
        
        /// <summary>
        /// True if the capture was blocked by a Forcefield skill.
        /// The attacker should bounce back to their original position.
        /// </summary>
        public bool CaptureBlocked { get; set; }
        
        /// <summary>
        /// The unit ID whose Forcefield was consumed (for visual feedback).
        /// </summary>
        public int? ForcefieldConsumedUnitId { get; set; }
        
        // Game state after move
        public bool GameEnded { get; set; }
        public int? WinnerId { get; set; }
        public GameEndReason EndReason { get; set; }
        
        // Turn info
        public int NewTurnNumber { get; set; }
        public int NewCurrentPlayerId { get; set; }
        
        public static MoveExecutionResult Failed(string error) => new MoveExecutionResult { Success = false, Error = error };
    }
    
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
        /// Result of attempting to execute a move, including capture blocking.
        /// </summary>
        public class MoveResult
        {
            public bool CaptureBlocked;
            public int? ForcefieldConsumedUnitId;
        }

        /// <summary>
        /// Execute a move action (chess-style: capture = eliminate).
        /// Returns info about whether the capture was blocked by skills like Forcefield.
        /// </summary>
        /// <param name="isRangedCapture">If true, the unit captures without moving (ranged attack)</param>
        public MoveResult ExecuteMove(int unitId, int targetNode, bool isCapture = false, int? capturedUnitId = null, bool isRangedCapture = false)
        {
            var result = new MoveResult();
            var unit = GetUnit(unitId);
            if (unit == null) return result;

            int fromNode = unit.CurrentNodeId;

            // Handle capture (chess-style: instant elimination)
            if (isCapture && capturedUnitId.HasValue)
            {
                var captured = GetUnit(capturedUnitId.Value);
                if (captured != null)
                {
                    // Check for Forcefield skill
                    var forcefield = captured.Skills?.OfType<ForcefieldSkill>().FirstOrDefault();
                    if (forcefield != null && forcefield.TryBlockCapture())
                    {
                        // Capture blocked! Attacker bounces back
                        result.CaptureBlocked = true;
                        result.ForcefieldConsumedUnitId = captured.UnitId;
                        
                        // Remove the consumed forcefield skill from the unit
                        captured.RemoveSkill("Forcefield");
                        
                        // Record the blocked capture action
                        ActionHistory.Add(new GameAction
                        {
                            Type = ActionType.CaptureBlocked,
                            UnitId = unitId,
                            FromNode = fromNode,
                            ToNode = targetNode,
                            TargetUnitId = capturedUnitId,
                            TurnNumber = TurnNumber,
                            PlayerId = CurrentPlayerId
                        });
                        
                        // Attacker doesn't move, turn still ends
                        unit.HasMovedThisTurn = true;
                        unit.HasActedThisTurn = true;
                        
                        return result;
                    }
                    
                    // No forcefield or already consumed - capture succeeds
                    captured.IsAlive = false;
                }
            }

            // Move unit (unless ranged capture - unit stays in place)
            if (!isRangedCapture)
            {
                unit.MoveTo(targetNode);
            }
            else
            {
                // Mark that unit has acted this turn even without moving
                unit.HasMovedThisTurn = true;
                unit.HasActedThisTurn = true;
            }

            // Record action
            ActionHistory.Add(new GameAction
            {
                Type = isRangedCapture ? ActionType.RangedCapture : ActionType.Move,
                UnitId = unitId,
                FromNode = fromNode,
                ToNode = isRangedCapture ? fromNode : targetNode, // Unit stays at fromNode for ranged
                CapturedUnitId = capturedUnitId,
                TurnNumber = TurnNumber,
                PlayerId = CurrentPlayerId
            });

            // Handle special tiles (only if unit actually moved)
            if (!isRangedCapture)
            {
                HandleNodeEffect(unit, targetNode);
            }

            return result;
        }

        /// <summary>
        /// Execute a complete move: validate, execute, check win conditions, and end turn.
        /// This is the single source of truth for move logic - use this from both offline and network modes.
        /// </summary>
        public MoveExecutionResult TryExecuteFullMove(int unitId, int targetNode, MoveValidator validator, int requestingPlayerId)
        {
            // Validate it's this player's turn
            if (requestingPlayerId != CurrentPlayerId)
            {
                return MoveExecutionResult.Failed("Not your turn");
            }

            // Get unit
            var unit = GetUnit(unitId);
            if (unit == null)
            {
                return MoveExecutionResult.Failed("Unit not found");
            }

            // Validate move
            var validation = validator.ValidateMove(unit, targetNode, Units, requestingPlayerId);
            if (!validation.IsValid)
            {
                return MoveExecutionResult.Failed(validation.Error);
            }

            // Execute the move
            int fromNode = unit.CurrentNodeId;
            var moveResult = ExecuteMove(unitId, targetNode, validation.IsCapture, validation.CapturedUnitId, validation.IsRangedCapture);

            // Check win conditions (only if capture wasn't blocked)
            if (!moveResult.CaptureBlocked)
            {
                CheckWinConditions(validator);
            }

            bool gameEnded = Phase == GamePhase.Ended;
            int? winnerId = WinnerId;
            GameEndReason endReason = EndReason;

            // End turn (if game hasn't ended)
            if (!gameEnded)
            {
                EndTurn();
            }

            return new MoveExecutionResult
            {
                Success = true,
                UnitId = unitId,
                FromNode = fromNode,
                ToNode = targetNode,
                IsCapture = validation.IsCapture && !moveResult.CaptureBlocked, // Only count as capture if not blocked
                CapturedUnitId = moveResult.CaptureBlocked ? null : validation.CapturedUnitId,
                IsRangedCapture = validation.IsRangedCapture,
                CaptureBlocked = moveResult.CaptureBlocked,
                ForcefieldConsumedUnitId = moveResult.ForcefieldConsumedUnitId,
                GameEnded = gameEnded,
                WinnerId = winnerId,
                EndReason = endReason,
                NewTurnNumber = TurnNumber,
                NewCurrentPlayerId = CurrentPlayerId
            };
        }

        private void HandleNodeEffect(UnitState unit, int nodeId)
        {
            var node = BoardState.GetNode(nodeId);

            switch (node.CurrentType)
            {
                case NodeType.Teleport:
                    if (node.TeleportTargetId >= 0 && BoardState.IsNodePassable(node.TeleportTargetId))
                    {
                        unit.CurrentNodeId = node.TeleportTargetId;
                    }
                    break;

                case NodeType.Unstable:
                    // Nothing immediate, but node might collapse
                    break;

                // Future: Boost and Trap effects will be handled via Skills
                case NodeType.Boost:
                case NodeType.Trap:
                    // TODO: Trigger skill-based effects
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
        public string AbilityId;
    }

    public enum ActionType
    {
        Move,
        RangedCapture, // Capture without moving (e.g., Crossbowman)
        CaptureBlocked, // Capture was blocked by Forcefield
        Ability,
        Spawn,
        EndTurn
    }

    #endregion
}
