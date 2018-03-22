using UnityEditor.Graphing;
using UnityEditor.ShaderGraph;

namespace ShaderGraphExtensions
{
    [Title("Input", "Scene", "Realtime Shadow")]
    public class RealtimeShadow : AbstractMaterialNode, IGeneratesBodyCode, IGeneratesFunction, IMayRequirePosition
    {
        public RealtimeShadow()
        {
            name = "Realtime Shadow";
            UpdateNodeAfterDeserialization();
        }

        public override string documentationURL
        {
            get { return "https://github.com/beinteractive/ShaderGraphExtensions"; }
        }

        const int OutputSlotId = 0;
        const string k_OutputSlotName = "Out";

        public override bool hasPreview
        {
            get { return false; }
        }

        string GetFunctionName()
        {
            return string.Format("sge_RealtimeShadow{0}", precision);
        }

        public sealed override void UpdateNodeAfterDeserialization()
        {
            AddSlot(new Vector1MaterialSlot(OutputSlotId, k_OutputSlotName, k_OutputSlotName, SlotType.Output, 0));
            RemoveSlotsNameNotMatching(new[] {OutputSlotId});
        }

        public void GenerateNodeCode(ShaderGenerator visitor, GenerationMode generationMode)
        {
            visitor.AddShaderChunk(string.Format("{0} {1};", FindOutputSlot<MaterialSlot>(OutputSlotId).concreteValueType.ToString(precision), GetVariableNameForSlot(OutputSlotId)), false);
            visitor.AddShaderChunk(string.Format("{0}(IN.{1}, {2});", GetFunctionName(),
                CoordinateSpace.World.ToVariableName(InterpolatorType.Position),
                GetVariableNameForSlot(OutputSlotId)), false);
        }

        public void GenerateNodeFunction(FunctionRegistry registry, GenerationMode generationMode)
        {
            registry.ProvideFunction(GetFunctionName(), s =>
            {
                s.AppendLine("#ifndef LIGHTWEIGHT_SHADOWS_INCLUDED");
                s.Append(@"
float4 ComputeShadowCoord(float4 clipPos)
{
    return (0.0).xxxx;
}

half RealtimeShadowAttenuation(float4 shadowCoord)
{
    return 1.0h;
}
");
                s.AppendLine("#endif");
                s.AppendLine("void {0}({1}3 WorldSpacePosition, out {2} Out)",
                    GetFunctionName(),
                    precision,
                    FindOutputSlot<MaterialSlot>(OutputSlotId).concreteValueType.ToString(precision));
                using (s.BlockScope())
                {
                    s.AppendLine("Out = RealtimeShadowAttenuation(ComputeShadowCoord(TransformWorldToHClip(WorldSpacePosition)));");
                }
            });
        }

        public NeededCoordinateSpace RequiresPosition()
        {
            return CoordinateSpace.World.ToNeededCoordinateSpace();
        }
    }
}