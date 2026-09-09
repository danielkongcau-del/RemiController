using System;
using Newtonsoft.Json.Linq;

namespace Remielle.ControllerRuntime
{
    public readonly struct NativeCondition
    {
        public readonly uint Mode;
        public readonly uint Parameter;
        public readonly float Threshold;
        // Retained from the source constant; the comparison predicate does not read it.
        public readonly float ExitTime;

        public NativeCondition(uint mode, uint parameter, float threshold, float exitTime = 0)
        {
            Mode = mode; Parameter = parameter; Threshold = threshold; ExitTime = exitTime;
        }

        public static NativeCondition FromSource(JArray fields)
        {
            if (fields == null || fields.Count != 4)
                throw new ArgumentException("Expected the four original condition fields");
            return new NativeCondition((uint)fields[0], (uint)fields[1], (float)fields[2], (float)fields[3]);
        }

        // UnityPlayer b8a21ad9..., RVA 0xcfe7c0. Modes 9/10 are game extensions.
        // See native-condition-evidence.json and the independently executed vectors.
        // This predicate reads parameters only; transition commitment owns trigger reset.
        public bool Evaluate(NativeParameterBank bank)
        {
            if (!bank.TryGetKind(Parameter, out int kind)) return false;
            if (Mode == 1)
                return kind == 4 ? bank.GetBool(Parameter) : kind == 9 && bank.GetTrigger(Parameter);
            if (Mode == 2) return kind == 4 && !bank.GetBool(Parameter);
            if (Mode == 6 || Mode == 7)
            {
                if (kind != 3) return false;
                // The original converts int32 to float32 before comparing, including
                // equality. Comparing as int/double would differ above 2^24.
                float integer = bank.GetInt(Parameter);
                return Mode == 6 ? integer == Threshold : integer != Threshold;
            }
            if (kind != 1 && kind != 3) return false;
            float value = kind == 1 ? bank.GetFloat(Parameter) : bank.GetInt(Parameter);
            switch (Mode)
            {
                case 3: return value > Threshold;
                case 4: return value < Threshold;
                case 9: return value >= Threshold;
                case 10: return value <= Threshold;
                default: return false;
            }
        }
    }
}
