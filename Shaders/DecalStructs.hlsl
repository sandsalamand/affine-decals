#ifndef URP_SURFACE_SHADER_INPUTS_INCLUDED
#define URP_SURFACE_SHADER_INPUTS_INCLUDED

// GLES2 has limited amount of interpolators
#if defined(_PARALLAXMAP) && !defined(SHADER_API_GLES)
#define REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR
#endif

#if (defined(_NORMALMAP) || (defined(_PARALLAXMAP) && !defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR))) || defined(_DETAIL)
#define REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR
#endif

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

#if defined(FORWARD_PASS) || defined(GBUFFER_PASS)

struct TrianglePoint
{
    float2 uv;
    float3 positionWS;    
    float3 normalWS;
};

TrianglePoint CreateEmptyTrianglePoint()
{
    TrianglePoint triPoint;         
    triPoint.uv = float2(0,0);      
    triPoint.positionWS = float3(0,0,0);
    triPoint.normalWS = float3(0,0,0);
    return triPoint;
}

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

#endif

/////////////////////////////////////////
// Forward Lighting
/////////////////////////////////////////

#if defined(FORWARD_PASS)

struct Attributes
{
    float4 positionOS   : POSITION;
    float4 positionCS   : TEXCOORD3;
    float3 normalOS     : NORMAL;
    float4 tangentOS    : TANGENT;
	float4 color		: COLOR;
    float2 texcoord     : TEXCOORD0;
    float2 staticLightmapUV   : TEXCOORD1;
    float2 dynamicLightmapUV  : TEXCOORD2;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    #include "SharedVertAndGeometry.hlsl"

#ifdef _ADDITIONAL_LIGHTS_VERTEX
    half4 fogFactorAndVertexLight   : TEXCOORD9; // x: fogFactor, yzw: vertex light
#else
    half  fogFactor                 : TEXCOORD9;
#endif
};

struct GeometryOut
{
    #include "SharedVertAndGeometry.hlsl"

#ifdef _ADDITIONAL_LIGHTS_VERTEX
    half4 fogFactorAndVertexLight   : TEXCOORD9; // x: fogFactor, yzw: vertex light
#else
    half  fogFactor                 : TEXCOORD9;
#endif

    // Offset each texcoord by 3 because each struct holds three members
    nointerpolation TrianglePoint trianglePoint1   :TEXCOORD10;
    nointerpolation TrianglePoint trianglePoint2   :TEXCOORD13;
    nointerpolation TrianglePoint trianglePoint3   :TEXCOORD16;
};

GeometryOut VertexToGeometryStandard(Varyings input)
{
    GeometryOut o;

    o.uv = input.uv;

#if defined(REQUIRES_WORLD_SPACE_POS_INTERPOLATOR)
    o.positionWS = input.positionWS;                
#endif

    o.normalWS = input.normalWS;                  
#if defined(REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR)
    o.tangentWS = input.tangentWS;                     
#endif
    o.viewDirWS = input.viewDirWS;                 

#ifdef _ADDITIONAL_LIGHTS_VERTEX
    o.fogFactorAndVertexLight = input.fogFactorAndVertexLight;
#else
    o.fogFactor = input.fogFactor;                  
#endif

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    o.shadowCoord = input.shadowCoord;               
#endif

#if defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
    o.viewDirTS = input.viewDirTS;                 
#endif

#if defined(LIGHTMAP_ON)
    o.staticLightmapUV = input.staticLightmapUV;
#else
    o.vertexSH = input.vertexSH;
#endif

#ifdef DYNAMICLIGHTMAP_ON
    o.dynamicLightmapUV = input.dynamicLightmapUV; 
#endif

#if defined(REQUIRES_VERTEX_COLOR)
    o.color = input.color;                		
#endif

    o.positionCS = input.positionCS;                

    UNITY_TRANSFER_INSTANCE_ID(input, o);

    //We have to initialize these structs even though they will be immediately overwritten
    o.trianglePoint1 = CreateEmptyTrianglePoint();
    o.trianglePoint2 = CreateEmptyTrianglePoint();
    o.trianglePoint3 = CreateEmptyTrianglePoint();

    return o;
}

/////////////////////////////////////////
// SHADOWS
/////////////////////////////////////////

#elif defined(SHADOWS_PASS)

struct Attributes
{
    float4 positionOS   : POSITION;
    float3 normalOS     : NORMAL;
    float2 texcoord     : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float2 uv           : TEXCOORD0;
    float4 positionCS   : SV_POSITION;
};

/////////////////////////////////////////
// G Buffer
/////////////////////////////////////////

#elif defined(GBUFFER_PASS)

struct Attributes
{
    float4 positionOS   : POSITION;
    float3 normalOS     : NORMAL;
    float4 tangentOS    : TANGENT;
	float4 color		: COLOR;
    float2 texcoord     : TEXCOORD0;
    float2 staticLightmapUV   : TEXCOORD1;
    float2 dynamicLightmapUV  : TEXCOORD2;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    #include "SharedVertAndGeometry.hlsl"

#ifdef _ADDITIONAL_LIGHTS_VERTEX
    half3 vertexLighting            : TEXCOORD9;    // xyz: vertex lighting
#endif
};

struct GeometryOut
{
    #include "SharedVertAndGeometry.hlsl"

#ifdef _ADDITIONAL_LIGHTS_VERTEX
    half3 vertexLighting            : TEXCOORD9;    // xyz: vertex lighting
#endif

    // Offset each texcoord by 3 because each struct holds three members
    nointerpolation TrianglePoint trianglePoint1   :TEXCOORD10;
    nointerpolation TrianglePoint trianglePoint2   :TEXCOORD13;
    nointerpolation TrianglePoint trianglePoint3   :TEXCOORD16;
};

GeometryOut VertexToGeometryStandard(Varyings input)
{
    GeometryOut o;

    o.uv = input.uv;

#if defined(REQUIRES_WORLD_SPACE_POS_INTERPOLATOR)
    o.positionWS = input.positionWS;                
#endif

    o.normalWS = input.normalWS;                  
#if defined(REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR)
    o.tangentWS = input.tangentWS;                     
#endif
    o.viewDirWS = input.viewDirWS;                 

#ifdef _ADDITIONAL_LIGHTS_VERTEX
    o.vertexLighting = input.vertexLighting;
#endif

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    o.shadowCoord = input.shadowCoord;               
#endif

#if defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
    o.viewDirTS = input.viewDirTS;                 
#endif

#if defined(LIGHTMAP_ON)
    o.staticLightmapUV = input.staticLightmapUV;
#else
    o.vertexSH = input.vertexSH;
#endif

#ifdef DYNAMICLIGHTMAP_ON
    o.dynamicLightmapUV = input.dynamicLightmapUV; 
#endif

#if defined(REQUIRES_VERTEX_COLOR)
    o.color = input.color;                		
#endif

    o.positionCS = input.positionCS;                

    UNITY_TRANSFER_INSTANCE_ID(input, o);

    //We have to initialize these structs even though they will be immediately overwritten
    o.trianglePoint1 = CreateEmptyTrianglePoint();
    o.trianglePoint2 = CreateEmptyTrianglePoint();
    o.trianglePoint3 = CreateEmptyTrianglePoint();

    return o;
}

/////////////////////////////////////////
// Depth Only
/////////////////////////////////////////

#elif defined(DEPTH_ONLY_PASS)

struct Attributes
{
    float4 positionOS     : POSITION;
    float2 texcoord     : TEXCOORD0;
	float3 normalOS       : NORMAL;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float2 uv           : TEXCOORD0;
    float4 positionCS   : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

/////////////////////////////////////////
// Depth Normals
/////////////////////////////////////////

#elif defined(DEPTH_NORMALS_PASS)

struct Attributes
{
    float4 positionOS   : POSITION;
    float4 tangentOS    : TANGENT;
    float2 texcoord     : TEXCOORD0;
    float3 normalOS       : NORMAL;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS   : SV_POSITION;
    float2 uv           : TEXCOORD1;
    float3 normalWS     : TEXCOORD2;

    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

/////////////////////////////////////////
// Meta
/////////////////////////////////////////

#elif defined(META_PASS)

struct Attributes
{
    float4 positionOS   : POSITION;
    float3 normalOS     : NORMAL;
    float2 texcoord     : TEXCOORD0;
    float2 texcoord2    : TEXCOORD1;
    float2 texcoord3    : TEXCOORD2;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS   : SV_POSITION;
    float2 uv           : TEXCOORD0;
#ifdef EDITOR_VISUALIZATION
    float2 VizUV        : TEXCOORD1;
    float4 LightCoord   : TEXCOORD2;
#endif
};

/////////////////////////////////////////
// 2D
/////////////////////////////////////////

#elif defined(UNIVERSAL_2D_PASS)

struct Attributes
{
    float4 positionOS       : POSITION;
	float3 normalOS     	: NORMAL;
    float2 texcoord         : TEXCOORD0;
};

struct Varyings
{
    float2 uv        	: TEXCOORD0;
    float4 vertex 		: SV_POSITION;
	
#if defined(REQUIRES_VERTEX_COLOR)
    float4 color        : COLOR;
#endif
};

#endif

#endif //URP_SURFACE_SHADER_INPUTS_INCLUDED
