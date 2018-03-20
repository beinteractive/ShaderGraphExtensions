using UnityEditor.Graphing;
using UnityEditor.ShaderGraph;
using UnityEngine;

namespace ShaderGraphExtensions
{
    [Title("Input", "Scene", "Main Light")]
    public class MainLight : AbstractMaterialNode, IGeneratesBodyCode, IGeneratesFunction, IMayRequirePosition
    {
        public MainLight()
        {
            name = "Main Light";
            UpdateNodeAfterDeserialization();
        }

        public override string documentationURL
        {
            get { return "https://github.com/beinteractive/ShaderGraphExtensions"; }
        }

        const int OutputSlotId = 0;
        const int OutputSlot1Id = 1;
        const int OutputSlot2Id = 2;
        const string k_OutputSlotName = "Attenuation";
        const string k_OutputSlot1Name = "Color";
        const string k_OutputSlot2Name = "Direction";

        public override bool hasPreview
        {
            get { return false; }
        }

        string GetFunctionName()
        {
            return string.Format("sge_MainLight{0}", precision);
        }

        public sealed override void UpdateNodeAfterDeserialization()
        {
            AddSlot(new Vector1MaterialSlot(OutputSlotId, k_OutputSlotName, k_OutputSlotName, SlotType.Output, 0));
            AddSlot(new Vector3MaterialSlot(OutputSlot1Id, k_OutputSlot1Name, k_OutputSlot1Name, SlotType.Output, Vector3.zero));
            AddSlot(new Vector3MaterialSlot(OutputSlot2Id, k_OutputSlot2Name, k_OutputSlot2Name, SlotType.Output, Vector3.zero));
            RemoveSlotsNameNotMatching(new[] {OutputSlotId, OutputSlot1Id, OutputSlot2Id});
        }

        public void GenerateNodeCode(ShaderGenerator visitor, GenerationMode generationMode)
        {
            visitor.AddShaderChunk(string.Format("{0} {1};", FindOutputSlot<MaterialSlot>(OutputSlotId).concreteValueType.ToString(precision), GetVariableNameForSlot(OutputSlotId)), false);
            visitor.AddShaderChunk(string.Format("{0} {1};", FindOutputSlot<MaterialSlot>(OutputSlot1Id).concreteValueType.ToString(precision), GetVariableNameForSlot(OutputSlot1Id)), false);
            visitor.AddShaderChunk(string.Format("{0} {1};", FindOutputSlot<MaterialSlot>(OutputSlot2Id).concreteValueType.ToString(precision), GetVariableNameForSlot(OutputSlot2Id)), false);
            visitor.AddShaderChunk(string.Format("{0}(IN.{1}, {2}, {3}, {4});", GetFunctionName(),
                CoordinateSpace.World.ToVariableName(InterpolatorType.Position),
                GetVariableNameForSlot(OutputSlotId), GetVariableNameForSlot(OutputSlot1Id), GetVariableNameForSlot(OutputSlot2Id)), false);
        }

        public void GenerateNodeFunction(FunctionRegistry registry, GenerationMode generationMode)
        {
            registry.ProvideFunction(GetFunctionName(), s =>
            {
                s.AppendLine("#ifndef LIGHTWEIGHT_LIGHTING_INCLUDED");
                s.Append(@"
// Abstraction over Light input constants
struct LightInput
{
    float4  position;
    half3   color;
    half4   distanceAttenuation;
    half4   spotDirection;
    half4   spotAttenuation;
};

// Abstraction over Light shading data.
struct Light
{
    half3   direction;
    half3   color;
    half    attenuation;
    half    subtractiveModeAttenuation;
};

// Matches Unity Vanila attenuation
// Attenuation smoothly decreases to light range.
half DistanceAttenuation(half distanceSqr, half3 distanceAttenuation)
{
    // We use a shared distance attenuation for additional directional and puctual lights
    // for directional lights attenuation will be 1
    half quadFalloff = distanceAttenuation.x;
    half denom = distanceSqr * quadFalloff + 1.0;
    half lightAtten = 1.0 / denom;

    // We need to smoothly fade attenuation to light range. We start fading linearly at 80% of light range
    // Therefore:
    // fadeDistance = (0.8 * 0.8 * lightRangeSq)
    // smoothFactor = (lightRangeSqr - distanceSqr) / (lightRangeSqr - fadeDistance)
    // We can rewrite that to fit a MAD by doing
    // distanceSqr * (1.0 / (fadeDistanceSqr - lightRangeSqr)) + (-lightRangeSqr / (fadeDistanceSqr - lightRangeSqr)
    // distanceSqr *        distanceAttenuation.y            +             distanceAttenuation.z
    half smoothFactor = saturate(distanceSqr * distanceAttenuation.y + distanceAttenuation.z);
    return lightAtten * smoothFactor;
}

half SpotAttenuation(half3 spotDirection, half3 lightDirection, half4 spotAttenuation)
{
    // Spot Attenuation with a linear falloff can be defined as
    // (SdotL - cosOuterAngle) / (cosInnerAngle - cosOuterAngle)
    // This can be rewritten as
    // invAngleRange = 1.0 / (cosInnerAngle - cosOuterAngle)
    // SdotL * invAngleRange + (-cosOuterAngle * invAngleRange)
    // SdotL * spotAttenuation.x + spotAttenuation.y

    // If we precompute the terms in a MAD instruction
    half SdotL = dot(spotDirection, lightDirection);
    half atten = saturate(SdotL * spotAttenuation.x + spotAttenuation.y);
    return atten * atten;
}

half4 GetLightDirectionAndAttenuation(LightInput lightInput, float3 positionWS)
{
    half4 directionAndAttenuation;
    float3 posToLightVec = lightInput.position.xyz - positionWS * lightInput.position.w;
    float distanceSqr = max(dot(posToLightVec, posToLightVec), FLT_MIN);

    directionAndAttenuation.xyz = half3(posToLightVec * rsqrt(distanceSqr));
    directionAndAttenuation.w = DistanceAttenuation(distanceSqr, lightInput.distanceAttenuation.xyz);
    directionAndAttenuation.w *= SpotAttenuation(lightInput.spotDirection.xyz, directionAndAttenuation.xyz, lightInput.spotAttenuation);
    return directionAndAttenuation;
}

half4 GetMainLightDirectionAndAttenuation(LightInput lightInput, float3 positionWS)
{
    half4 directionAndAttenuation = GetLightDirectionAndAttenuation(lightInput, positionWS);

    // Cookies are only computed for main light
    // directionAndAttenuation.w *= CookieAttenuation(positionWS);

    return directionAndAttenuation;
}

Light GetMainLight(float3 positionWS)
{
    LightInput lightInput;
    lightInput.position = float4(-0.3213938, 0.7660444, -0.5566704, 0);
    lightInput.color = half3(1, 1, 1);
    lightInput.distanceAttenuation = half4(0, 1, 0, 0);
    lightInput.spotDirection = half4(0, 0, 1, 0);
    lightInput.spotAttenuation = half4(0, 1, 0, 1);

    half4 directionAndRealtimeAttenuation = GetMainLightDirectionAndAttenuation(lightInput, positionWS);

    Light light;
    light.direction = directionAndRealtimeAttenuation.xyz;
    light.attenuation = directionAndRealtimeAttenuation.w;
    light.subtractiveModeAttenuation = lightInput.distanceAttenuation.w;
    light.color = lightInput.color;

    return light;
}
");
                s.AppendLine("#endif");
                s.AppendLine("void {0}({1}3 WorldSpacePosition, out {2} Attenuation, out {3} Color, out {4} Direction)",
                    GetFunctionName(),
                    precision,
                    FindOutputSlot<MaterialSlot>(OutputSlotId).concreteValueType.ToString(precision),
                    FindOutputSlot<MaterialSlot>(OutputSlot1Id).concreteValueType.ToString(precision),
                    FindOutputSlot<MaterialSlot>(OutputSlot2Id).concreteValueType.ToString(precision));
                using (s.BlockScope())
                {
                    s.AppendLine("Light mainLight = GetMainLight(WorldSpacePosition);");
                    s.AppendLine("Attenuation = mainLight.attenuation;");
                    s.AppendLine("Color = mainLight.color;");
                    s.AppendLine("Direction = mainLight.direction;");
                }
            });
        }

        public NeededCoordinateSpace RequiresPosition()
        {
            return CoordinateSpace.World.ToNeededCoordinateSpace();
        }
    }
}