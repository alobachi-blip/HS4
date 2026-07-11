// ---- Created with 3Dmigoto v1.3.16 on Sat Jul 11 23:01:41 2026
cbuffer cb2 : register(b2)
{
  float4 cb2[21];
}

cbuffer cb1 : register(b1)
{
  float4 cb1[10];
}

cbuffer cb0 : register(b0)
{
  float4 cb0[29];
}




// 3Dmigoto declarations
#define cmp -


void main(
  float4 v0 : POSITION0,
  float4 v1 : TANGENT0,
  float3 v2 : NORMAL0,
  float4 v3 : TEXCOORD0,
  float4 v4 : TEXCOORD1,
  float4 v5 : TEXCOORD2,
  float4 v6 : TEXCOORD3,
  float4 v7 : COLOR0,
  out float4 o0 : SV_POSITION0,
  out float4 o1 : TEXCOORD0,
  out float4 o2 : TEXCOORD1,
  out float4 o3 : TEXCOORD2,
  out float4 o4 : TEXCOORD3,
  out float4 o5 : COLOR0,
  out float4 o6 : TEXCOORD6,
  out float4 o7 : TEXCOORD7)
{
  float4 r0,r1,r2,r3;
  uint4 bitmask, uiDest;
  float4 fDest;

  r0.xyzw = cb1[1].xyzw * v0.yyyy;
  r0.xyzw = cb1[0].xyzw * v0.xxxx + r0.xyzw;
  r0.xyzw = cb1[2].xyzw * v0.zzzz + r0.xyzw;
  r1.xyzw = cb1[3].xyzw + r0.xyzw;
  r0.xyz = cb1[3].xyz * v0.www + r0.xyz;
  r2.xyzw = cb2[18].xyzw * r1.yyyy;
  r2.xyzw = cb2[17].xyzw * r1.xxxx + r2.xyzw;
  r2.xyzw = cb2[19].xyzw * r1.zzzz + r2.xyzw;
  o0.xyzw = cb2[20].xyzw * r1.wwww + r2.xyzw;
  o1.xy = v3.xy * cb0[27].xy + cb0[27].zw;
  o1.zw = v4.xy * cb0[28].xy + cb0[28].zw;
  o2.w = r0.x;
  r1.y = dot(v2.xyz, cb1[4].xyz);
  r1.z = dot(v2.xyz, cb1[5].xyz);
  r1.x = dot(v2.xyz, cb1[6].xyz);
  r0.x = dot(r1.xyz, r1.xyz);
  r0.x = rsqrt(r0.x);
  r1.xyz = r1.xyz * r0.xxx;
  r2.xyz = cb1[1].yzx * v1.yyy;
  r2.xyz = cb1[0].yzx * v1.xxx + r2.xyz;
  r2.xyz = cb1[2].yzx * v1.zzz + r2.xyz;
  r0.x = dot(r2.xyz, r2.xyz);
  r0.x = rsqrt(r0.x);
  r2.xyz = r2.xyz * r0.xxx;
  r3.xyz = r2.xyz * r1.xyz;
  r3.xyz = r1.zxy * r2.yzx + -r3.xyz;
  r0.x = cb1[9].w * v1.w;
  r3.xyz = r3.xyz * r0.xxx;
  o2.y = r3.x;
  o2.x = r2.z;
  o2.z = r1.y;
  o3.x = r2.x;
  o4.x = r2.y;
  o3.z = r1.z;
  o4.z = r1.x;
  o3.w = r0.y;
  o4.w = r0.z;
  o3.y = r3.y;
  o4.y = r3.z;
  o5.xyzw = v7.xyzw;
  o6.xyzw = float4(0,0,0,0);
  o7.xyzw = float4(0,0,0,0);
  return;
}