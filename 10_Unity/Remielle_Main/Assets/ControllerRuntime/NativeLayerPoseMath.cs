using System;
using System.Runtime.InteropServices;

namespace Remielle.ControllerRuntime
{
    // cf87d0: ordered layer composition, distinct from state weighted averaging.
    public static class NativeLayerPoseMath
    {
        [DllImport("RemielleLayerPose", EntryPoint="remielle_layer_pose_abi", CallingConvention=CallingConvention.Cdecl)]
        public static extern int Abi();
        [DllImport("RemielleLayerPose", EntryPoint="remielle_layer_quaternions", CallingConvention=CallingConvention.Cdecl)]
        static extern int Quaternions([In] float[] defaults, [In] float[] source, [In] byte[] mask,
            [In,Out] float[] output, [In,Out] byte[] outputMask, int count, float weight, int additive);

        public static void Compose(NativePoseData defaults, NativePoseStream source,
            float weight, bool additive, NativePoseStream output)
        {
            if (source == null || output == null) throw new ArgumentNullException();
            if (!ReferenceEquals(source.BindingLayout, output.BindingLayout))
                throw new ArgumentException("Layer streams belong to different binding layouts");
            output.CheckPose(source.Data); output.CheckPose(defaults);
            // Check the helper before changing any output channels.
            if (Abi() != 1) throw new InvalidOperationException("Unqualified layer quaternion helper");
            for (int g = 0; g < 5; g++)
            {
                if (g == 1)
                {
                    if (Quaternions(defaults.Floats[1],source.Data.Floats[1],source.Active[1],
                        output.Data.Floats[1],output.Active[1],output.Data.Counts[1],weight,additive?1:0) != 0)
                        throw new InvalidOperationException("Layer quaternion composition failed");
                    continue;
                }
                for (int i = 0; i < output.Data.Counts[g]; i++)
                {
                    if (source.Active[g][i] == 0) continue;
                    var basis = output.Active[g][i] != 0 ? output.Data : defaults;
                    if (g == 4)
                    {
                        // cf8fdb: <= half (and unordered) retains base data but
                        // clears the written bit, even if that bit was set.
                        bool select = weight > .5f;
                        output.Data.Discrete[i] = (select ? source.Data : basis).Discrete[i];
                        output.Active[g][i] = (byte)(select ? 1 : 0);
                        continue;
                    }
                    int width = g < 3 ? 4 : 1;
                    for (int lane = 0; lane < width; lane++)
                    {
                        int j = i*width+lane;
                        float value = source.Data.Floats[g][j], prior = basis.Floats[g][j];
                        if (additive) value = (float)((float)(value*weight)+prior);
                        else if (weight < 1f)
                        {
                            // Scalar interpolation has a different arithmetic
                            // order from the four-lane translation/scale path.
                            if (g == 3) value = (float)((float)((float)(1f-weight)*prior)+(float)(value*weight));
                            else value = (float)((float)((float)(value-prior)*weight)+prior);
                        }
                        output.Data.Floats[g][j] = value;
                    }
                    output.Active[g][i] = 1;
                }
            }
            // Ordinary layer math does not update stream/reference flags.
        }
    }
}
