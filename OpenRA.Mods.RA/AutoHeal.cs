﻿#region Copyright & License Information
/*
 * Copyright 2007-2010 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made 
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation. For more information,
 * see LICENSE.
 */
#endregion

using System.Linq;
using OpenRA.Traits;
using OpenRA.Traits.Activities;

namespace OpenRA.Mods.RA
{
	class AutoHealInfo : TraitInfo<AutoHeal> { }

	class AutoHeal : INotifyIdle
	{
		public void TickIdle( Actor self )
		{
			var attack = self.Trait<AttackBase>();
			var inRange = self.World.FindUnitsInCircle(self.CenterLocation, Game.CellSize * attack.GetMaximumRange());

			var target = inRange
				.Where(a => a != self && self.Owner.Stances[a.Owner] == Stance.Ally)
				.Where(a => a.IsInWorld && !a.IsDead())
				.Where(a => a.HasTrait<Health>() && a.GetDamageState() > DamageState.Undamaged)
				.Where(a => attack.HasAnyValidWeapons(Target.FromActor(a)))
				.OrderBy(a => (a.CenterLocation - self.CenterLocation).LengthSquared)
				.FirstOrDefault();
			
			if( target != null )
				self.QueueActivity(self.Trait<AttackBase>().GetAttackActivity(self, Target.FromActor( target ), false ));
		}
	}
}