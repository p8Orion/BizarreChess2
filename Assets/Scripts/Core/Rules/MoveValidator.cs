using System.Collections.Generic;
using System.Linq;
using BizarreChess.Core.Board;
using BizarreChess.Core.Units;

// Re-export MoveTargets for convenience
using MoveTargets = BizarreChess.Core.Units.MoveTargets;

namespace BizarreChess.Core.Rules
{
    /// <summary>
    /// Validates moves according to game rules.
    /// </summary>
    public class MoveValidator
    {
        private readonly BoardGraph _board;
        private readonly Dictionary<string, UnitDefinition> _unitDefinitions;

        public MoveValidator(BoardGraph board, Dictionary<string, UnitDefinition> unitDefinitions)
        {
            _board = board;
            _unitDefinitions = unitDefinitions;
        }

        /// <summary>
        /// Check if a unit can move to a target node.
        /// </summary>
        public MoveValidationResult ValidateMove(
            UnitState unit,
            int targetNode,
            List<UnitState> allUnits,
            int currentPlayerId)
        {
            // Basic checks
            if (!unit.IsAlive)
                return MoveValidationResult.Fail("Unit is dead");

            if (unit.OwnerId != currentPlayerId)
                return MoveValidationResult.Fail("Not your unit");

            if (unit.HasMovedThisTurn)
                return MoveValidationResult.Fail("Unit has already moved this turn");

            if (!_board.IsPassable(targetNode))
                return MoveValidationResult.Fail("Target node is not passable");

            // Get unit definition
            if (!_unitDefinitions.TryGetValue(unit.DefinitionId, out var definition))
                return MoveValidationResult.Fail("Unknown unit type");

            // Get categorized moves to check for ranged captures
            var categorizedMoves = GetCategorizedMovesForUnit(unit, definition, allUnits);
            var validMoves = categorizedMoves.GetAll();

            if (!validMoves.Contains(targetNode))
                return MoveValidationResult.Fail("Invalid move for this unit type");

            // Check if this is a ranged capture or capture-only square
            bool isRangedCapture = categorizedMoves.IsRangedCapture(targetNode);
            bool isCaptureOnly = categorizedMoves.CaptureOnly.Contains(targetNode);
            bool canMoveToEmpty = categorizedMoves.MoveOnly.Contains(targetNode);

            // Check if target is occupied
            var targetOccupant = allUnits.FirstOrDefault(u => u.IsAlive && u.CurrentNodeId == targetNode);
            
            if (targetOccupant != null)
            {
                if (targetOccupant.OwnerId == currentPlayerId)
                    return MoveValidationResult.Fail("Cannot move to a square occupied by your own unit");

                // This is a capture (possibly ranged)
                return MoveValidationResult.Success(isCapture: true, capturedUnitId: targetOccupant.UnitId, isRangedCapture: isRangedCapture);
            }

            // CaptureOnly and RangedCapture squares require an enemy to be valid moves
            // UNLESS the node is also in MoveOnly (e.g., Crossbowman can move to adjacent diagonals)
            if ((isCaptureOnly || isRangedCapture) && !canMoveToEmpty)
                return MoveValidationResult.Fail("Can only capture here, not move to empty square");

            return MoveValidationResult.Success();
        }

        /// <summary>
        /// Get all valid move targets for a unit.
        /// </summary>
        public List<int> GetValidMovesForUnit(UnitState unit, UnitDefinition definition, List<UnitState> allUnits)
        {
            return GetCategorizedMovesForUnit(unit, definition, allUnits).GetAll();
        }

        /// <summary>
        /// Get categorized valid moves for a unit (move-only, capture-only, both).
        /// </summary>
        public MoveTargets GetCategorizedMovesForUnit(UnitState unit, UnitDefinition definition, List<UnitState> allUnits)
        {
            bool IsOccupied(int nodeId) => allUnits.Any(u => u.IsAlive && u.CurrentNodeId == nodeId);
            bool IsEnemy(int nodeId) => allUnits.Any(u => u.IsAlive && u.CurrentNodeId == nodeId && u.OwnerId != unit.OwnerId);

            return definition.GetAllCategorizedMoves(
                _board,
                unit.CurrentNodeId,
                unit.OwnerId,
                IsOccupied,
                IsEnemy,
                unit.HasEverMoved
            );
        }

        /// <summary>
        /// Check if a unit can attack another unit (for games with separate attack action).
        /// </summary>
        public AttackValidationResult ValidateAttack(
            UnitState attacker,
            UnitState target,
            int currentPlayerId)
        {
            if (!attacker.IsAlive)
                return AttackValidationResult.Fail("Attacker is dead");

            if (!target.IsAlive)
                return AttackValidationResult.Fail("Target is dead");

            if (attacker.OwnerId != currentPlayerId)
                return AttackValidationResult.Fail("Not your unit");

            if (target.OwnerId == currentPlayerId)
                return AttackValidationResult.Fail("Cannot attack your own unit");

            if (attacker.HasActedThisTurn)
                return AttackValidationResult.Fail("Unit has already acted this turn");

            // Check range
            int distance = _board.GetDistance(attacker.CurrentNodeId, target.CurrentNodeId);
            if (distance < 0 || distance > attacker.Range)
                return AttackValidationResult.Fail("Target is out of range");

            return AttackValidationResult.Success();
        }

        /// <summary>
        /// Check if a player's king is in check.
        /// </summary>
        public bool IsKingInCheck(int playerId, List<UnitState> allUnits)
        {
            var king = allUnits.FirstOrDefault(u => 
                u.IsAlive && 
                u.OwnerId == playerId && 
                _unitDefinitions.TryGetValue(u.DefinitionId, out var def) && 
                def.IsKing);

            if (king == null)
                return false; // No king = can't be in check (or already lost)

            // Check if any enemy unit can capture the king
            foreach (var enemy in allUnits.Where(u => u.IsAlive && u.OwnerId != playerId))
            {
                if (!_unitDefinitions.TryGetValue(enemy.DefinitionId, out var enemyDef))
                    continue;

                var validMoves = GetValidMovesForUnit(enemy, enemyDef, allUnits);
                if (validMoves.Contains(king.CurrentNodeId))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Check if a player is in checkmate.
        /// </summary>
        public bool IsCheckmate(int playerId, List<UnitState> allUnits)
        {
            if (!IsKingInCheck(playerId, allUnits))
                return false;

            // Check if any move can get out of check
            foreach (var unit in allUnits.Where(u => u.IsAlive && u.OwnerId == playerId))
            {
                if (!_unitDefinitions.TryGetValue(unit.DefinitionId, out var definition))
                    continue;

                var categorizedMoves = GetCategorizedMovesForUnit(unit, definition, allUnits);
                var validMoves = categorizedMoves.GetAll();
                
                foreach (var move in validMoves)
                {
                    // Check if this is a ranged capture or capture-only (unit doesn't move)
                    bool isRanged = categorizedMoves.IsRangedCapture(move);
                    bool isCaptureOnly = categorizedMoves.CaptureOnly.Contains(move);
                    bool canMoveToEmpty = categorizedMoves.MoveOnly.Contains(move);
                    
                    // Check if there's an enemy to capture
                    var capturedUnit = allUnits.FirstOrDefault(u => u.IsAlive && u.CurrentNodeId == move);
                    
                    // CaptureOnly and RangedCapture squares require an enemy - skip if empty
                    // UNLESS the node is also in MoveOnly (e.g., Crossbowman adjacent diagonals)
                    if ((isCaptureOnly || isRanged) && capturedUnit == null && !canMoveToEmpty)
                        continue;
                    
                    // Simulate the move
                    int originalNode = unit.CurrentNodeId;
                    
                    // Only move unit if not a ranged capture with actual enemy
                    bool isActualRangedCapture = isRanged && capturedUnit != null;
                    if (!isActualRangedCapture)
                        unit.CurrentNodeId = move;
                    
                    if (capturedUnit != null)
                        capturedUnit.IsAlive = false;

                    bool stillInCheck = IsKingInCheck(playerId, allUnits);

                    // Undo the move
                    unit.CurrentNodeId = originalNode;
                    if (capturedUnit != null)
                        capturedUnit.IsAlive = true;

                    if (!stillInCheck)
                        return false; // Found a way out
                }
            }

            return true; // No way out = checkmate
        }

        /// <summary>
        /// Check if a player is in stalemate (no legal moves but not in check).
        /// </summary>
        public bool IsStalemate(int playerId, List<UnitState> allUnits)
        {
            if (IsKingInCheck(playerId, allUnits))
                return false;

            // Check if player has any legal moves
            foreach (var unit in allUnits.Where(u => u.IsAlive && u.OwnerId == playerId))
            {
                if (!_unitDefinitions.TryGetValue(unit.DefinitionId, out var definition))
                    continue;

                var categorizedMoves = GetCategorizedMovesForUnit(unit, definition, allUnits);
                
                // Check if there are any truly valid moves (excluding CaptureOnly/RangedCapture on empty squares)
                if (HasAnyValidMove(categorizedMoves, allUnits, unit.OwnerId))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Check if there are any truly valid moves (CaptureOnly/RangedCapture only valid if enemy present).
        /// </summary>
        private bool HasAnyValidMove(MoveTargets moves, List<UnitState> allUnits, int playerId)
        {
            // MoveOnly and Both are always valid (they're on empty squares or can capture)
            if (moves.MoveOnly.Count > 0 || moves.Both.Count > 0)
                return true;

            // CaptureOnly squares are valid only if occupied by enemy
            foreach (var nodeId in moves.CaptureOnly)
            {
                var occupant = allUnits.FirstOrDefault(u => u.IsAlive && u.CurrentNodeId == nodeId);
                if (occupant != null && occupant.OwnerId != playerId)
                    return true;
            }

            // RangedCapture squares are valid only if occupied by enemy
            foreach (var nodeId in moves.RangedCapture)
            {
                var occupant = allUnits.FirstOrDefault(u => u.IsAlive && u.CurrentNodeId == nodeId);
                if (occupant != null && occupant.OwnerId != playerId)
                    return true;
            }

            return false;
        }
    }

    public class MoveValidationResult
    {
        public bool IsValid;
        public string Error;
        public bool IsCapture;
        public bool IsRangedCapture; // Capture without moving the attacking unit
        public int? CapturedUnitId;

        public static MoveValidationResult Success(bool isCapture = false, int? capturedUnitId = null, bool isRangedCapture = false)
        {
            return new MoveValidationResult
            {
                IsValid = true,
                IsCapture = isCapture,
                IsRangedCapture = isRangedCapture,
                CapturedUnitId = capturedUnitId
            };
        }

        public static MoveValidationResult Fail(string error)
        {
            return new MoveValidationResult
            {
                IsValid = false,
                Error = error
            };
        }
    }

    public class AttackValidationResult
    {
        public bool IsValid;
        public string Error;

        public static AttackValidationResult Success()
        {
            return new AttackValidationResult { IsValid = true };
        }

        public static AttackValidationResult Fail(string error)
        {
            return new AttackValidationResult { IsValid = false, Error = error };
        }
    }
}

