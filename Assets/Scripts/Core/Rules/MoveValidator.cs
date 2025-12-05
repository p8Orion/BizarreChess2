using System.Collections.Generic;
using System.Linq;
using BizarreChess.Core.Graph;
using BizarreChess.Core.Units;

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

            // Get valid moves for this unit
            var validMoves = GetValidMovesForUnit(unit, definition, allUnits);

            if (!validMoves.Contains(targetNode))
                return MoveValidationResult.Fail("Invalid move for this unit type");

            // Check if target is occupied
            var targetOccupant = allUnits.FirstOrDefault(u => u.IsAlive && u.CurrentNodeId == targetNode);
            
            if (targetOccupant != null)
            {
                if (targetOccupant.OwnerId == currentPlayerId)
                    return MoveValidationResult.Fail("Cannot move to a square occupied by your own unit");

                // This is a capture
                return MoveValidationResult.Success(isCapture: true, capturedUnitId: targetOccupant.UnitId);
            }

            return MoveValidationResult.Success();
        }

        /// <summary>
        /// Get all valid move targets for a unit.
        /// </summary>
        public List<int> GetValidMovesForUnit(UnitState unit, UnitDefinition definition, List<UnitState> allUnits)
        {
            bool IsOccupied(int nodeId) => allUnits.Any(u => u.IsAlive && u.CurrentNodeId == nodeId);
            bool IsEnemy(int nodeId) => allUnits.Any(u => u.IsAlive && u.CurrentNodeId == nodeId && u.OwnerId != unit.OwnerId);

            return definition.GetAllValidMoves(
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

                var validMoves = GetValidMovesForUnit(unit, definition, allUnits);
                
                foreach (var move in validMoves)
                {
                    // Simulate the move
                    int originalNode = unit.CurrentNodeId;
                    var capturedUnit = allUnits.FirstOrDefault(u => u.IsAlive && u.CurrentNodeId == move);
                    
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

                var validMoves = GetValidMovesForUnit(unit, definition, allUnits);
                if (validMoves.Count > 0)
                    return false;
            }

            return true;
        }
    }

    public class MoveValidationResult
    {
        public bool IsValid;
        public string Error;
        public bool IsCapture;
        public int? CapturedUnitId;

        public static MoveValidationResult Success(bool isCapture = false, int? capturedUnitId = null)
        {
            return new MoveValidationResult
            {
                IsValid = true,
                IsCapture = isCapture,
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

