using System;

namespace BizarreChess.Core.Skills
{
    /// <summary>
    /// A protective shield that blocks one capture attempt, then is consumed.
    /// When active, prevents the unit from being captured and causes the attacker to bounce back.
    /// </summary>
    [Serializable]
    public class ForcefieldSkill : Skill
    {
        /// <summary>
        /// Whether the forcefield is still active and can block a capture.
        /// </summary>
        public bool IsActive = true;

        public ForcefieldSkill()
        {
            Id = "Forcefield";
            DisplayName = "Forcefield";
            Description = "Blocks one capture attempt, then is consumed. The attacker bounces back to their original position.";
        }

        /// <summary>
        /// Attempt to block an incoming capture.
        /// Returns true if the capture was blocked (forcefield consumed).
        /// Returns false if the forcefield was already consumed.
        /// </summary>
        public bool TryBlockCapture()
        {
            if (!IsActive)
                return false;

            IsActive = false; // Consume the forcefield
            return true;
        }

        public override Skill Clone()
        {
            return new ForcefieldSkill
            {
                Id = this.Id,
                DisplayName = this.DisplayName,
                Description = this.Description,
                IsActive = this.IsActive
            };
        }
    }
}

