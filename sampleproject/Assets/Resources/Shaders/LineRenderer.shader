Shader "LineRenderer"
{
	Properties
	{
		offsetX ("offsetX", Float) = 0
		offsetY ("offsetY", Float) = 0
		screenWidth ("screenWidth", Float) = 0
		screenHeight ("screenHeight", Float) = 0
	}
	SubShader
	{
		Tags { "Queue"="Transparent" "RenderType"="Transparent" }
		LOD 100

		Pass
		{
			ZWrite Off
			Blend SrcAlpha OneMinusSrcAlpha
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 5.0

			struct Line
			{
				float2 start;
				float2 end;
				float4 color;
				float width;
			};
			StructuredBuffer<Line> lines;
			struct v2f
			{
				float4 vertex : SV_POSITION;
				float4 color : COLOR0;
				float2 lineStart : TEXCOORD0;
				float2 lineEnd : TEXCOORD1;
				float3 linePos : TEXCOORD2;
			};

			float offsetX;
			float offsetY;
			float screenWidth;
			float screenHeight;

			v2f vert (uint id : SV_VertexID, uint inst : SV_InstanceID)
			{

				uint lineId = id / 6;
				uint subId = id - lineId*6;

				float lineWidth = lines[lineId].width / 2;
				float2 lineStart = lines[lineId].start - float2(offsetX, offsetY);
				float2 lineEnd = lines[lineId].end - float2(offsetX, offsetY);

				float2 lineDir = normalize(lineEnd - lineStart);
				float2 lineNorm = cross(float3(lineDir,0), float3(0,0,1)).xy;

				// Expand by a pixel in each direction
				lineNorm = normalize(lineNorm) * (lineWidth+1);

				v2f o;
				float2 vpos = lineStart - lineDir * (lineWidth + 1);
				if (subId == 1 || subId == 3 || subId == 4)
					vpos = lineEnd + lineDir * (lineWidth + 1);
				if (subId == 0 || subId == 1 || subId == 3)
					vpos += lineNorm;
				else
					vpos -= lineNorm;



				o.linePos = float3(vpos, lineWidth);

				vpos.x = ((vpos.x * 2) / screenWidth) - 1;
				vpos.y = 1 - ((vpos.y * 2) / screenHeight);

				o.vertex = float4(vpos, 0, 1);
				o.color = lines[lineId].color;

				o.lineStart = lineStart;
				o.lineEnd = lineEnd;
				return o;
			}

			fixed4 frag (v2f i) : SV_Target
			{
				float2 vpos = i.linePos.xy;


				// Share this and pass as parameter?
				float2 dir = normalize(i.lineEnd - i.lineStart);
				float startDist = dot(dir, i.lineStart);
				float endDist = dot(dir, i.lineEnd);
				float2 norm = normalize(cross(float3(dir,0), float3(0,0,1)).xy);
				float normDist = dot(norm, i.lineStart);

				float4 color = i.color;
				// dist to line, assuming inside
				float alpha = abs(dot(norm, vpos) - normDist);

				float capDist = dot(dir, vpos);
				float startCapDist = step(capDist-startDist, 0) * length(i.lineStart - vpos);
				float endCapDist = step(endDist-capDist, 0) * length(i.lineEnd - vpos);
				alpha = max(alpha, startCapDist);
				alpha = max(alpha, endCapDist);

				alpha -= (i.linePos.z-0.5);
				color.a *= 1-saturate(alpha);

				return fixed4(color);
			}
			ENDCG
		}
	}
}
