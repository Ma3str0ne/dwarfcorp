﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DwarfCorp.Goals
{
    public enum GoalTypes
    {
        Achievement,
        Active
    }

    public enum GoalState
    {
        Unavailable,
        Available,
        Active,
        Complete
    }
}
