Shader "Unlit/anim2"
{
    Properties
    {
        _MainTex ("主贴图", 2D) = "white" {}
        _LightTex("高光贴图",2D)="white"{}
        _BaseColor("叠加色",Color)=(1,1,1,1)

        _DarkSideEdge("暗部范围",Range(-1,1))=0
        _DarksideSmooth("暗部边缘过渡",Range(0,1))=0
        _DarkSideBrightness("暗部亮度",Range(0,1))=1

        _HighLightEdge("高光范围",Range(-1,1))=0
        _HighLightSmooth("高光过渡",Range(0,1))=0.1
        _HighLightPower("高光Power值",Range(1,86))=1

        _BDEdge("明暗分割线范围",Range(0,1))=0
        _BDEdgePos("明暗分割线位置",Range(-1,1))=0
        _BDsmooth("明暗分割线过渡",Range(0,1))=0
        _BDColorValue("明暗分割线饱和值",Range(0,10))=0
        
        _Factor("描边宽度",Range(0,1)) = 0.01		
		_OutLineColor("描边颜色",Color) = (0,0,0,1)
    }
    SubShader
    {
        Tags { "RenderType"="ForwardBase" }
        LOD 100
        Pass
	{
		Cull Front //剔除前面
		CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "UnityCG.cginc"
 
		struct v2f
	{
		float4 vertex :POSITION;
	};
   
	float _Factor;
	half4 _OutLineColor;
 
	v2f vert(appdata_full v)
	{
		v2f o;
		//将顶点沿法线方向向外扩展一下
		float4 pos=v.vertex+float4(v.normal*_Factor,1.0);
        o.vertex=UnityObjectToClipPos(pos);
 
		return o;
	}
 
	half4 frag(v2f v) :COLOR
	{
		//只显示描边的颜色
		return _OutLineColor;
	}
		ENDCG
	}

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Lighting.cginc"

            float _BDEdge;
            float _HighLightSmooth;
            float _DarksideSmooth;
            float _BDsmooth;
            float4 _BDEdgeColor;
            float _BDEdgePos;
            float _DarkSideEdge;
            sampler2D _MainTex;
            sampler2D _LightTex;
            float4 _MainTex_ST;
            float4 _LightTex_ST;
            fixed4 _BaseColor;
            float _DarkSideBrightness;
            float _HighLightEdge;
            float _BDColorValue;
            float _HighLightPower;
            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal:NORMAL;
                float2 uv:TEXCOORD0;
            };

            struct v2f
            {
                float2 uv:TEXCOORD0;
                float3 worldNormal:TEXCOORD1;
                float3 worldPos:TEXCOORD2;
                float2 uvlight:TEXCOORD3;
                float4 vertex : POSITION;
  
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex=UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld,v.vertex).xyz;//获取世界空间的顶点位置
                o.worldNormal=mul(unity_ObjectToWorld,v.normal);//获取世界空间的法线向量
                o.uv=TRANSFORM_TEX(v.uv,_MainTex);
                o.uvlight=TRANSFORM_TEX(v.uv,_LightTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed3 lightTex=tex2D(_LightTex,i.uvlight);
                
                float3 lightDir=normalize(UnityWorldSpaceLightDir(i.worldPos));
                float3 viewDir=normalize(UnityWorldSpaceViewDir(i.worldPos));
                // sample the texture
                float3 worldNormal=normalize(i.worldNormal);

                float diffuseV=dot(worldNormal,lightDir);
                
                fixed4 surfacecolor=tex2D(_MainTex,i.uv)*_BaseColor;
                float diffStep=smoothstep(-_DarksideSmooth,_DarksideSmooth,diffuseV-_DarkSideEdge);
                float BDedge=(1-smoothstep(-_BDsmooth,_BDsmooth,abs(diffuseV-_BDEdgePos)-_BDEdge));//技术难题1：如何移动此光圈的位置
                float tmp=1-diffStep;
                float ds=tmp*_DarkSideBrightness;

                float highLightV=pow(dot(viewDir,reflect(-lightDir,worldNormal)),_HighLightPower);
                float hlvSp=smoothstep(-_HighLightSmooth,_HighLightSmooth,highLightV-_HighLightEdge);
                float bv=Luminance(surfacecolor);
                fixed4 bv4=fixed4(bv,bv,bv,0);
                
                fixed4 dsColor=(UNITY_LIGHTMODEL_AMBIENT+ds*surfacecolor*_BaseColor);
                return (_LightColor0*surfacecolor*diffStep+dsColor+hlvSp*fixed4(1,1,1,1)*_LightColor0*fixed4(lightTex,1))+BDedge*(surfacecolor-bv4)*_BDColorValue;
            }
            ENDCG
        }                                                                                                                                                
    }
    Fallback "Specular"
}
