using System;
using System.Collections.Generic;

namespace Chronos.Core.Domain.Character
{
    public interface IPartRegistry
    {
        EquipmentPart? GetPart(int partId);
        void Register(EquipmentPart part);
    }
}