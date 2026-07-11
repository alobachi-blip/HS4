// ---- Created with 3Dmigoto v1.3.16 on Sat Jul 11 23:05:37 2026
Texture2D<float4> t16 : register(t16);

Texture2D<float4> t15 : register(t15);

Texture2D<float4> t14 : register(t14);

Texture2D<float4> t13 : register(t13);

Texture2D<float4> t12 : register(t12);

Texture2D<float4> t11 : register(t11);

Texture2D<float4> t10 : register(t10);

Texture2D<float4> t9 : register(t9);

Texture2D<float4> t8 : register(t8);

Texture2D<float4> t7 : register(t7);

Texture2D<float4> t6 : register(t6);

Texture2D<float4> t5 : register(t5);

Texture3D<float4> t4 : register(t4);

TextureCube<float4> t3 : register(t3);

TextureCube<float4> t2 : register(t2);

Texture2D<float4> t1 : register(t1);

Texture2D<float4> t0 : register(t0);

SamplerState s14_s : register(s14);

SamplerState s13_s : register(s13);

SamplerState s12_s : register(s12);

SamplerState s11_s : register(s11);

SamplerState s10_s : register(s10);

SamplerState s9_s : register(s9);

SamplerState s8_s : register(s8);

SamplerState s7_s : register(s7);

SamplerState s6_s : register(s6);

SamplerState s5_s : register(s5);

SamplerState s4_s : register(s4);

SamplerState s3_s : register(s3);

SamplerState s2_s : register(s2);

SamplerState s1_s : register(s1);

SamplerState s0_s : register(s0);

cbuffer cb5 : register(b5)
{
  float4 cb5[7];
}

cbuffer cb4 : register(b4)
{
  float4 cb4[8];
}

cbuffer cb3 : register(b3)
{
  float4 cb3[22];
}

cbuffer cb2 : register(b2)
{
  float4 cb2[47];
}

cbuffer cb1 : register(b1)
{
  float4 cb1[5];
}

cbuffer cb0 : register(b0)
{
  float4 cb0[36];
}




// 3Dmigoto declarations
#define cmp -


void main(
  float4 v0 : SV_POSITION0,
  float4 v1 : TEXCOORD0,
  float4 v2 : TEXCOORD1,
  float4 v3 : TEXCOORD2,
  float4 v4 : TEXCOORD3,
  float4 v5 : TEXCOORD4,
  float4 v6 : COLOR0,
  float4 v7 : TEXCOORD7,
  float4 v8 : TEXCOORD8,
  out float4 o0 : SV_Target0)
{
  float4 r0,r1,r2,r3,r4,r5,r6,r7,r8,r9,r10,r11,r12,r13,r14,r15,r16,r17,r18,r19,r20,r21,r22,r23,r24,r25,r26,r27,r28;
  uint4 bitmask, uiDest;
  float4 fDest;

  r0.x = v2.w;
  r0.y = v3.w;
  r0.z = v4.w;
  r1.xyz = cb1[4].xyz + -r0.xyz;
  r0.w = dot(r1.xyz, r1.xyz);
  r0.w = rsqrt(r0.w);
  r2.xyz = r1.xyz * r0.www;
  r3.xyzw = t5.Sample(s7_s, v1.xy).xyzw;
  r4.xy = cb0[20].xy + v1.zw;
  r1.w = 3.14159274 * cb0[21].x;
  sincos(r1.w, r5.x, r6.x);
  r4.xy = float2(-0.300000012,-0.400000006) + r4.xy;
  r7.x = -r5.x;
  r7.y = r6.x;
  r7.z = r5.x;
  r5.x = dot(r4.xy, r7.yz);
  r5.y = dot(r4.xy, r7.xy);
  r4.xyzw = float4(0.200000018,-0.0300000012,0.200000018,-0.0300000012) + r5.xyxy;
  r4.xyzw = r4.xyzw * cb0[20].zwzw + float4(0.100000001,0.430000007,0.1008,0.430800021);
  r5.xy = t14.Sample(s12_s, r4.xy).yz;
  r1.w = cb0[19].w * r5.y;
  r1.w = 1.5 * r1.w;
  r1.w = v6.y * r1.w;
  r6.xyz = cb0[19].xyz + -r3.xyz;
  r6.xyz = r1.www * r6.xyz + r3.xyz;
  r5.zw = t11.Sample(s9_s, v1.xy).yz;
  r2.w = cb0[23].x * r5.w;
  r2.w = cb0[22].w * r2.w;
  r7.xyz = cb0[22].xyz + -r6.xyz;
  r6.xyz = r2.www * r7.xyz + r6.xyz;
  r6.xyz = cb0[10].xyz * r6.xyz;
  r2.w = 1 + -cb0[23].y;
  r7.xyz = t12.Sample(s9_s, v1.xy).xyz;
  r5.w = 1 + -r7.y;
  r6.w = 1 + -r5.w;
  r5.w = cb0[23].z * r6.w + r5.w;
  r6.w = ceil(cb0[23].y);
  r5.w = r6.w * r5.w;
  r6.w = r7.x * r5.w;
  r7.x = 1 + -r2.w;
  r6.w = r6.w * cb0[23].w + -r2.w;
  r7.x = 1 / r7.x;
  r6.w = saturate(r7.x * r6.w);
  r7.y = r6.w * -2 + 3;
  r6.w = r6.w * r6.w;
  r7.w = r7.y * r6.w;
  r8.x = 3.14159274 * cb0[24].z;
  r8.yz = cb0[24].yy * v1.xy;
  r9.xy = floor(r8.yz);
  r8.yz = frac(r8.yz);
  r8.w = 8;
  r9.z = -1;
  while (true) {
    r9.w = cmp(1 < (int)r9.z);
    if (r9.w != 0) break;
    r10.y = (int)r9.z;
    r9.w = r8.w;
    r10.z = -1;
    while (true) {
      r10.w = cmp(1 < (int)r10.z);
      if (r10.w != 0) break;
      r10.x = (int)r10.z;
      r11.xy = r10.xy + r9.xy;
      r10.w = dot(r11.xy, float2(127.099998,311.700012));
      r11.x = dot(r11.xy, float2(269.5,183.300003));
      r12.x = sin(r10.w);
      r12.y = sin(r11.x);
      r11.xy = float2(43758.5469,43758.5469) * r12.xy;
      r11.xy = frac(r11.xy);
      r11.xy = r11.xy * float2(6.28310013,6.28310013) + r8.xx;
      r11.xy = sin(r11.xy);
      r11.xy = r11.xy * float2(0.5,0.5) + float2(0.5,0.5);
      r10.xw = -r10.xy + r8.yz;
      r10.xw = r10.xw + -r11.xy;
      r10.x = dot(r10.xw, r10.xw);
      r10.x = 0.5 * r10.x;
      r10.w = cmp(r10.x < r9.w);
      r9.w = r10.w ? r10.x : r9.w;
      r10.z = (int)r10.z + 1;
    }
    r8.w = r9.w;
    r9.z = (int)r9.z + 1;
  }
  r8.w = cb0[24].x / r8.w;
  r8.w = saturate(1 + -r8.w);
  r10.xy = v1.xy;
  r10.z = 0;
  r9.zw = cb0[25].xx * r10.xy;
  r9.z = dot(r9.zw, float2(0.333333343,0.333333343));
  r11.xyz = r10.xyz * cb0[25].xxx + r9.zzz;
  r11.xyz = floor(r11.xyz);
  r12.xyz = r10.xyz * cb0[25].xxx + -r11.xyz;
  r9.z = dot(r11.xyz, float3(0.166666672,0.166666672,0.166666672));
  r12.xyz = r12.xyz + r9.zzz;
  r13.xyz = cmp(r12.zxy >= r12.xyz);
  r14.xyz = r13.yzx ? float3(1,1,1) : 0;
  r13.xyz = r13.xyz ? float3(0,0,0) : float3(1,1,1);
  r15.xyz = min(r14.xyz, r13.xyz);
  r13.xyz = max(r14.yzx, r13.yzx);
  r14.xyz = -r15.xyz + r12.xyz;
  r14.xyz = float3(0.166666672,0.166666672,0.166666672) + r14.xyz;
  r16.xyz = -r13.zxy + r12.xyz;
  r16.xyz = float3(0.333333343,0.333333343,0.333333343) + r16.xyz;
  r17.xyz = float3(-0.5,-0.5,-0.5) + r12.xyz;
  r18.xyz = float3(0.00346020772,0.00346020772,0.00346020772) * r11.xyz;
  r18.xyz = floor(r18.xyz);
  r11.xyz = -r18.xyz * float3(289,289,289) + r11.xyz;
  r18.xw = float2(0,1);
  r18.y = r15.z;
  r18.z = r13.y;
  r18.xyzw = r18.xyzw + r11.zzzz;
  r19.xyzw = r18.xyzw * float4(34,34,34,34) + float4(1,1,1,1);
  r18.xyzw = r19.xyzw * r18.xyzw;
  r19.xyzw = float4(0.00346020772,0.00346020772,0.00346020772,0.00346020772) * r18.xyzw;
  r19.xyzw = floor(r19.xyzw);
  r18.xyzw = -r19.xyzw * float4(289,289,289,289) + r18.xyzw;
  r18.xyzw = r18.xyzw + r11.yyyy;
  r19.xw = float2(0,1);
  r19.y = r15.y;
  r19.z = r13.x;
  r18.xyzw = r19.xyzw + r18.xyzw;
  r19.xyzw = r18.xyzw * float4(34,34,34,34) + float4(1,1,1,1);
  r18.xyzw = r19.xyzw * r18.xyzw;
  r19.xyzw = float4(0.00346020772,0.00346020772,0.00346020772,0.00346020772) * r18.xyzw;
  r19.xyzw = floor(r19.xyzw);
  r18.xyzw = -r19.xyzw * float4(289,289,289,289) + r18.xyzw;
  r11.xyzw = r18.xyzw + r11.xxxx;
  r13.xw = float2(0,1);
  r13.y = r15.x;
  r11.xyzw = r13.xyzw + r11.xyzw;
  r13.xyzw = r11.xyzw * float4(34,34,34,34) + float4(1,1,1,1);
  r11.xyzw = r13.xyzw * r11.xyzw;
  r13.xyzw = float4(0.00346020772,0.00346020772,0.00346020772,0.00346020772) * r11.xyzw;
  r13.xyzw = floor(r13.xyzw);
  r11.xyzw = -r13.xyzw * float4(289,289,289,289) + r11.xyzw;
  r13.xyzw = float4(0.0204081628,0.0204081628,0.0204081628,0.0204081628) * r11.xyzw;
  r13.xyzw = floor(r13.xyzw);
  r11.xyzw = -r13.xyzw * float4(49,49,49,49) + r11.xyzw;
  r13.xyzw = float4(0.142857149,0.142857149,0.142857149,0.142857149) * r11.xyzw;
  r13.xyzw = floor(r13.xyzw);
  r11.xyzw = -r13.xyzw * float4(7,7,7,7) + r11.xyzw;
  r13.xyzw = r13.xyzw * float4(2,2,2,2) + float4(0.5,0.5,0.5,0.5);
  r13.xyzw = r13.xyzw * float4(0.142857149,0.142857149,0.142857149,0.142857149) + float4(-1,-1,-1,-1);
  r11.xyzw = r11.xyzw * float4(2,2,2,2) + float4(0.5,0.5,0.5,0.5);
  r11.xyzw = r11.xzyw * float4(0.142857149,0.142857149,0.142857149,0.142857149) + float4(-1,-1,-1,-1);
  r15.xyzw = float4(1,1,1,1) + -abs(r13.xyzw);
  r15.xyzw = r15.xywz + -abs(r11.xzwy);
  r18.xz = floor(r13.xy);
  r18.yw = floor(r11.xz);
  r18.xyzw = r18.xyzw * float4(2,2,2,2) + float4(1,1,1,1);
  r19.xz = floor(r13.zw);
  r19.yw = floor(r11.yw);
  r19.xyzw = r19.xyzw * float4(2,2,2,2) + float4(1,1,1,1);
  r20.xyzw = cmp(float4(0,0,0,0) >= r15.xywz);
  r20.xyzw = r20.xyzw ? float4(-1,-1,-1,-1) : float4(-0,-0,-0,-0);
  r21.xz = r13.xy;
  r21.yw = r11.xz;
  r18.xyzw = r18.zwxy * r20.yyxx + r21.zwxy;
  r11.xz = r13.zw;
  r11.xyzw = r19.xyzw * r20.zzww + r11.xyzw;
  r13.xy = r18.zw;
  r13.z = r15.x;
  r19.x = dot(r13.xyz, r13.xyz);
  r18.z = r15.y;
  r19.y = dot(r18.xyz, r18.xyz);
  r20.xy = r11.xy;
  r20.z = r15.w;
  r19.z = dot(r20.xyz, r20.xyz);
  r15.xy = r11.zw;
  r19.w = dot(r15.xyz, r15.xyz);
  r11.xyzw = -r19.xyzw * float4(0.853734732,0.853734732,0.853734732,0.853734732) + float4(1.79284286,1.79284286,1.79284286,1.79284286);
  r13.xyz = r13.xyz * r11.xxx;
  r18.xyz = r18.xyz * r11.yyy;
  r11.xyz = r20.xyz * r11.zzz;
  r15.xyz = r15.xyz * r11.www;
  r19.x = dot(r12.xyz, r12.xyz);
  r19.y = dot(r14.xyz, r14.xyz);
  r19.z = dot(r16.xyz, r16.xyz);
  r19.w = dot(r17.xyz, r17.xyz);
  r19.xyzw = float4(0.600000024,0.600000024,0.600000024,0.600000024) + -r19.xyzw;
  r19.xyzw = max(float4(0,0,0,0), r19.xyzw);
  r19.xyzw = r19.xyzw * r19.xyzw;
  r19.xyzw = r19.xyzw * r19.xyzw;
  r12.x = dot(r12.xyz, r13.xyz);
  r12.y = dot(r14.xyz, r18.xyz);
  r12.z = dot(r16.xyz, r11.xyz);
  r12.w = dot(r17.xyz, r15.xyz);
  r9.z = dot(r19.xyzw, r12.xyzw);
  r9.z = r9.z * 21 + 0.5;
  r9.z = cb0[24].w / r9.z;
  r9.w = 1 + -cb0[24].w;
  r9.z = r9.z * r9.z + -cb0[24].w;
  r9.w = 1 / r9.w;
  r9.z = saturate(r9.z * r9.w);
  r11.x = r9.z * -2 + 3;
  r9.z = r9.z * r9.z;
  r9.z = r11.x * r9.z;
  r9.z = min(1, r9.z);
  r11.x = r9.z * r8.w;
  r11.y = r11.x * r5.z;
  r11.y = r11.y * r5.w;
  r11.y = r11.y * cb0[25].y + -r2.w;
  r11.y = saturate(r11.y * r7.x);
  r11.z = r11.y * -2 + 3;
  r11.y = r11.y * r11.y;
  r11.y = r11.z * r11.y;
  r7.z = cb0[23].y * r7.z;
  r11.z = r7.y * r6.w + r11.y;
  r11.z = cb0[25].z * r7.z + r11.z;
  r11.z = cb0[18].w * r11.z;
  r12.xyz = cb0[18].xyz * r6.xyz + -r6.xyz;
  r6.xyz = saturate(r11.zzz * r12.xyz + r6.xyz);
  r12.xyzw = v1.xyxy * cb0[27].xyxy + float4(0,-0.0299999993,0.00404771557,-0.0259522833);
  r11.zw = t15.Sample(s13_s, r12.xy).yz;
  r13.x = t10.Sample(s6_s, v1.xy).y;
  r13.y = max(cb0[28].x, cb0[28].y);
  r13.z = cmp(0 < r13.y);
  r13.z = r13.z ? 1.000000 : 0;
  r13.z = r13.x * r13.z;
  r13.w = cmp(r13.y >= 0.50999999);
  r14.x = cmp(1 >= r13.y);
  r13.w = r13.w ? r14.x : 0;
  r14.x = r13.z * r11.w;
  r13.w = r13.w ? -0 : -0.0500000007;
  r13.z = saturate(r11.z * r13.z + r13.w);
  r14.yzw = cb0[26].xyz + -r6.xyz;
  r6.xyz = r13.zzz * r14.yzw + r6.xyz;
  r6.xyz = float3(-1,-1,-1) + r6.xyz;
  r6.xyz = cb0[6].yyy * r6.xyz + float3(1,1,1);
  r13.z = dot(r6.xyz, float3(0.0396819152,0.45802179,0.00609653955));
  r14.y = 6 * r13.z;
  r6.xyz = -r13.zzz * float3(6,6,6) + r6.xyz;
  r6.xyz = cb0[7].xxx * r6.xyz + r14.yyy;
  r13.z = 0.000100000005 + v5.w;
  r14.yz = v5.xy / r13.zz;
  if (cb3[21].x == 0) {
    r15.xyz = t0.Sample(s2_s, r14.yz).xyz;
  } else {
    r15.xyz = t1.Sample(s3_s, r14.yz).xyz;
  }
  r14.yz = t6.Sample(s4_s, v1.xy).xy;
  r13.z = -1 + r14.z;
  r14.z = cb0[8].x * r13.z + 1;
  r16.xy = cb0[28].zw * v1.xy;
  r17.xy = t16.Sample(s14_s, r16.xy).yw;
  r14.w = v6.y * r5.y;
  r15.w = cmp(cb0[23].y == 0.000000);
  r17.z = cb0[5].z * 1.33333337 + 1;
  r17.z = 1 + -r17.z;
  r15.w = r15.w ? r17.z : 1.20000005;
  r15.w = r15.w * r5.z;
  r5.y = -r5.y * v6.y + 1;
  r5.y = r15.w * r5.y;
  r16.zw = v1.xy * cb0[28].zw + float2(0.00899999961,0.00899999961);
  r15.w = t16.Sample(s14_s, r16.zy).y;
  r15.w = r15.w + -r17.x;
  r18.x = r15.w * r5.y;
  r15.w = t16.Sample(s14_s, r16.xw).y;
  r15.w = r15.w + -r17.x;
  r18.y = r15.w * r5.y;
  r18.z = 0;
  r16.xyz = float3(0,0,1) + -r18.xyz;
  r5.y = dot(r16.xyz, r16.xyz);
  r5.y = rsqrt(r5.y);
  r16.xyz = r16.xyz * r5.yyy;
  r18.zw = v1.xy * cb0[29].xy + cb0[29].zw;
  r17.xz = log2(cb0[30].xy);
  r17.xz = float2(3.4000001,5.63999987) * r17.xz;
  r17.xz = exp2(r17.xz);
  r18.xy = r17.xx * float2(0.5,0.5) + r18.zw;
  r5.y = t12.Sample(s9_s, r18.zw).x;
  r15.w = t12.Sample(s9_s, r18.xw).x;
  r15.w = r15.w + -r5.y;
  r19.x = r15.w * r5.w;
  r15.w = t12.Sample(s9_s, r18.zy).x;
  r5.y = r15.w + -r5.y;
  r19.y = r5.y * r5.w;
  r19.z = 0;
  r18.xyz = float3(0,0,1) + -r19.xyz;
  r5.y = dot(r18.xyz, r18.xyz);
  r5.y = rsqrt(r5.y);
  r19.xyz = t8.Sample(s11_s, v1.xy).xyw;
  r19.x = r19.x * r19.z;
  r17.xw = r19.xy * float2(2,2) + float2(-1,-1);
  r19.xy = cb0[4].ww * r17.xw;
  r15.w = dot(r19.xy, r19.xy);
  r15.w = min(1, r15.w);
  r15.w = 1 + -r15.w;
  r15.w = sqrt(r15.w);
  r19.xyz = t9.Sample(s8_s, v1.xy).xyw;
  r19.x = r19.x * r19.z;
  r19.xy = r19.xy * float2(2,2) + float2(-1,-1);
  r19.xy = cb0[5].xx * r19.xy;
  r16.w = dot(r19.xy, r19.xy);
  r16.w = min(1, r16.w);
  r16.w = 1 + -r16.w;
  r16.w = sqrt(r16.w);
  r19.xy = r17.xw * cb0[4].ww + r19.xy;
  r19.z = r16.w * r15.w;
  r15.w = dot(r19.xyz, r19.xyz);
  r15.w = rsqrt(r15.w);
  r16.w = r19.z * r15.w;
  r19.xy = r19.xy * r15.ww + r16.xy;
  r19.z = r16.w * r16.z;
  r15.w = dot(r19.xyz, r19.xyz);
  r15.w = rsqrt(r15.w);
  r16.xyz = r19.xyz * r15.www;
  r18.xyz = r18.xyz * r5.yyy + -r16.xyz;
  r16.xyz = r7.www * r18.xyz + r16.xyz;
  r18.xy = r17.zz * float2(0.400000006,0.400000006) + v1.xy;
  r18.zw = r10.yz;
  r17.xz = cb0[25].xx * r18.xz;
  r5.y = dot(r17.xz, float2(0.333333343,0.333333343));
  r17.xzw = r18.xzw * cb0[25].xxx + r5.yyy;
  r17.xzw = floor(r17.xzw);
  r18.xzw = r18.xzw * cb0[25].xxx + -r17.xzw;
  r5.y = dot(r17.xzw, float3(0.166666672,0.166666672,0.166666672));
  r18.xzw = r18.xzw + r5.yyy;
  r19.xyz = cmp(r18.wxz >= r18.xzw);
  r20.xyz = r19.yzx ? float3(1,1,1) : 0;
  r19.xyz = r19.xyz ? float3(0,0,0) : float3(1,1,1);
  r21.xyz = min(r20.xyz, r19.xyz);
  r19.xyz = max(r20.yzx, r19.yzx);
  r20.xyz = -r21.xyz + r18.xzw;
  r20.xyz = float3(0.166666672,0.166666672,0.166666672) + r20.xyz;
  r22.xyz = -r19.zxy + r18.xzw;
  r22.xyz = float3(0.333333343,0.333333343,0.333333343) + r22.xyz;
  r23.xyz = float3(-0.5,-0.5,-0.5) + r18.xzw;
  r24.xyz = float3(0.00346020772,0.00346020772,0.00346020772) * r17.xzw;
  r24.xyz = floor(r24.xyz);
  r17.xzw = -r24.xyz * float3(289,289,289) + r17.xzw;
  r24.xw = float2(0,1);
  r24.y = r21.z;
  r24.z = r19.y;
  r24.xyzw = r24.xyzw + r17.wwww;
  r25.xyzw = r24.xyzw * float4(34,34,34,34) + float4(1,1,1,1);
  r24.xyzw = r25.xyzw * r24.xyzw;
  r25.xyzw = float4(0.00346020772,0.00346020772,0.00346020772,0.00346020772) * r24.xyzw;
  r25.xyzw = floor(r25.xyzw);
  r24.xyzw = -r25.xyzw * float4(289,289,289,289) + r24.xyzw;
  r24.xyzw = r24.xyzw + r17.zzzz;
  r25.xw = float2(0,1);
  r25.y = r21.y;
  r25.z = r19.x;
  r24.xyzw = r25.xyzw + r24.xyzw;
  r25.xyzw = r24.xyzw * float4(34,34,34,34) + float4(1,1,1,1);
  r24.xyzw = r25.xyzw * r24.xyzw;
  r25.xyzw = float4(0.00346020772,0.00346020772,0.00346020772,0.00346020772) * r24.xyzw;
  r25.xyzw = floor(r25.xyzw);
  r24.xyzw = -r25.xyzw * float4(289,289,289,289) + r24.xyzw;
  r24.xyzw = r24.xyzw + r17.xxxx;
  r19.xw = float2(0,1);
  r19.y = r21.x;
  r19.xyzw = r24.xyzw + r19.xyzw;
  r21.xyzw = r19.xyzw * float4(34,34,34,34) + float4(1,1,1,1);
  r19.xyzw = r21.xyzw * r19.xyzw;
  r21.xyzw = float4(0.00346020772,0.00346020772,0.00346020772,0.00346020772) * r19.xyzw;
  r21.xyzw = floor(r21.xyzw);
  r19.xyzw = -r21.xyzw * float4(289,289,289,289) + r19.xyzw;
  r21.xyzw = float4(0.0204081628,0.0204081628,0.0204081628,0.0204081628) * r19.xyzw;
  r21.xyzw = floor(r21.xyzw);
  r19.xyzw = -r21.xyzw * float4(49,49,49,49) + r19.xyzw;
  r21.xyzw = float4(0.142857149,0.142857149,0.142857149,0.142857149) * r19.xyzw;
  r21.xyzw = floor(r21.xyzw);
  r19.xyzw = -r21.xyzw * float4(7,7,7,7) + r19.xyzw;
  r21.xyzw = r21.xyzw * float4(2,2,2,2) + float4(0.5,0.5,0.5,0.5);
  r21.xyzw = r21.xyzw * float4(0.142857149,0.142857149,0.142857149,0.142857149) + float4(-1,-1,-1,-1);
  r19.xyzw = r19.xyzw * float4(2,2,2,2) + float4(0.5,0.5,0.5,0.5);
  r19.xyzw = r19.xzyw * float4(0.142857149,0.142857149,0.142857149,0.142857149) + float4(-1,-1,-1,-1);
  r24.xyzw = float4(1,1,1,1) + -abs(r21.xyzw);
  r24.xyzw = r24.xywz + -abs(r19.xzwy);
  r25.xz = floor(r21.xy);
  r25.yw = floor(r19.xz);
  r25.xyzw = r25.xyzw * float4(2,2,2,2) + float4(1,1,1,1);
  r26.xz = floor(r21.zw);
  r26.yw = floor(r19.yw);
  r26.xyzw = r26.xyzw * float4(2,2,2,2) + float4(1,1,1,1);
  r27.xyzw = cmp(float4(0,0,0,0) >= r24.xywz);
  r27.xyzw = r27.xyzw ? float4(-1,-1,-1,-1) : float4(-0,-0,-0,-0);
  r28.xz = r21.xy;
  r28.yw = r19.xz;
  r25.xyzw = r25.zwxy * r27.yyxx + r28.zwxy;
  r19.xz = r21.zw;
  r19.xyzw = r26.xyzw * r27.zzww + r19.xyzw;
  r21.xy = r25.zw;
  r21.z = r24.x;
  r26.x = dot(r21.xyz, r21.xyz);
  r25.z = r24.y;
  r26.y = dot(r25.xyz, r25.xyz);
  r27.xy = r19.xy;
  r27.z = r24.w;
  r26.z = dot(r27.xyz, r27.xyz);
  r24.xy = r19.zw;
  r26.w = dot(r24.xyz, r24.xyz);
  r19.xyzw = -r26.xyzw * float4(0.853734732,0.853734732,0.853734732,0.853734732) + float4(1.79284286,1.79284286,1.79284286,1.79284286);
  r17.xzw = r21.xyz * r19.xxx;
  r21.xyz = r25.xyz * r19.yyy;
  r19.xyz = r27.xyz * r19.zzz;
  r24.xyz = r24.xyz * r19.www;
  r25.x = dot(r18.xzw, r18.xzw);
  r25.y = dot(r20.xyz, r20.xyz);
  r25.z = dot(r22.xyz, r22.xyz);
  r25.w = dot(r23.xyz, r23.xyz);
  r25.xyzw = float4(0.600000024,0.600000024,0.600000024,0.600000024) + -r25.xyzw;
  r25.xyzw = max(float4(0,0,0,0), r25.xyzw);
  r25.xyzw = r25.xyzw * r25.xyzw;
  r25.xyzw = r25.xyzw * r25.xyzw;
  r26.x = dot(r18.xzw, r17.xzw);
  r26.y = dot(r20.xyz, r21.xyz);
  r26.z = dot(r22.xyz, r19.xyz);
  r26.w = dot(r23.xyz, r24.xyz);
  r5.y = dot(r25.xyzw, r26.xyzw);
  r5.y = r5.y * 21 + 0.5;
  r5.y = cb0[24].w / r5.y;
  r5.y = r5.y * r5.y + -cb0[24].w;
  r5.y = saturate(r5.y * r9.w);
  r7.w = r5.y * -2 + 3;
  r5.y = r5.y * r5.y;
  r5.y = r7.w * r5.y;
  r5.y = min(1, r5.y);
  r7.w = r8.w * r5.y + -r11.x;
  r19.x = r7.w * r5.w;
  r10.w = r18.y;
  r17.xz = cb0[25].xx * r10.xw;
  r7.w = dot(r17.xz, float2(0.333333343,0.333333343));
  r17.xzw = r10.xwz * cb0[25].xxx + r7.www;
  r17.xzw = floor(r17.xzw);
  r10.xyz = r10.xwz * cb0[25].xxx + -r17.xzw;
  r7.w = dot(r17.xzw, float3(0.166666672,0.166666672,0.166666672));
  r10.xyz = r10.xyz + r7.www;
  r18.xyz = cmp(r10.zxy >= r10.xyz);
  r20.xyz = r18.yzx ? float3(1,1,1) : 0;
  r18.xyz = r18.xyz ? float3(0,0,0) : float3(1,1,1);
  r21.xyz = min(r20.xyz, r18.xyz);
  r18.xyz = max(r20.yzx, r18.yzx);
  r20.xyz = -r21.xyz + r10.xyz;
  r20.xyz = float3(0.166666672,0.166666672,0.166666672) + r20.xyz;
  r22.xyz = -r18.zxy + r10.xyz;
  r22.xyz = float3(0.333333343,0.333333343,0.333333343) + r22.xyz;
  r23.xyz = float3(-0.5,-0.5,-0.5) + r10.xyz;
  r24.xyz = float3(0.00346020772,0.00346020772,0.00346020772) * r17.xzw;
  r24.xyz = floor(r24.xyz);
  r17.xzw = -r24.xyz * float3(289,289,289) + r17.xzw;
  r24.xw = float2(0,1);
  r24.y = r21.z;
  r24.z = r18.y;
  r24.xyzw = r24.xyzw + r17.wwww;
  r25.xyzw = r24.xyzw * float4(34,34,34,34) + float4(1,1,1,1);
  r24.xyzw = r25.xyzw * r24.xyzw;
  r25.xyzw = float4(0.00346020772,0.00346020772,0.00346020772,0.00346020772) * r24.xyzw;
  r25.xyzw = floor(r25.xyzw);
  r24.xyzw = -r25.xyzw * float4(289,289,289,289) + r24.xyzw;
  r24.xyzw = r24.xyzw + r17.zzzz;
  r25.xw = float2(0,1);
  r25.y = r21.y;
  r25.z = r18.x;
  r24.xyzw = r25.xyzw + r24.xyzw;
  r25.xyzw = r24.xyzw * float4(34,34,34,34) + float4(1,1,1,1);
  r24.xyzw = r25.xyzw * r24.xyzw;
  r25.xyzw = float4(0.00346020772,0.00346020772,0.00346020772,0.00346020772) * r24.xyzw;
  r25.xyzw = floor(r25.xyzw);
  r24.xyzw = -r25.xyzw * float4(289,289,289,289) + r24.xyzw;
  r24.xyzw = r24.xyzw + r17.xxxx;
  r18.xw = float2(0,1);
  r18.y = r21.x;
  r18.xyzw = r24.xyzw + r18.xyzw;
  r21.xyzw = r18.xyzw * float4(34,34,34,34) + float4(1,1,1,1);
  r18.xyzw = r21.xyzw * r18.xyzw;
  r21.xyzw = float4(0.00346020772,0.00346020772,0.00346020772,0.00346020772) * r18.xyzw;
  r21.xyzw = floor(r21.xyzw);
  r18.xyzw = -r21.xyzw * float4(289,289,289,289) + r18.xyzw;
  r21.xyzw = float4(0.0204081628,0.0204081628,0.0204081628,0.0204081628) * r18.xyzw;
  r21.xyzw = floor(r21.xyzw);
  r18.xyzw = -r21.xyzw * float4(49,49,49,49) + r18.xyzw;
  r21.xyzw = float4(0.142857149,0.142857149,0.142857149,0.142857149) * r18.xyzw;
  r21.xyzw = floor(r21.xyzw);
  r18.xyzw = -r21.xyzw * float4(7,7,7,7) + r18.xyzw;
  r21.xyzw = r21.xyzw * float4(2,2,2,2) + float4(0.5,0.5,0.5,0.5);
  r21.xyzw = r21.xyzw * float4(0.142857149,0.142857149,0.142857149,0.142857149) + float4(-1,-1,-1,-1);
  r18.xyzw = r18.xyzw * float4(2,2,2,2) + float4(0.5,0.5,0.5,0.5);
  r18.xyzw = r18.xzyw * float4(0.142857149,0.142857149,0.142857149,0.142857149) + float4(-1,-1,-1,-1);
  r24.xyzw = float4(1,1,1,1) + -abs(r21.xyzw);
  r24.xyzw = r24.xywz + -abs(r18.xzwy);
  r25.xz = floor(r21.xy);
  r25.yw = floor(r18.xz);
  r25.xyzw = r25.xyzw * float4(2,2,2,2) + float4(1,1,1,1);
  r26.xz = floor(r21.zw);
  r26.yw = floor(r18.yw);
  r26.xyzw = r26.xyzw * float4(2,2,2,2) + float4(1,1,1,1);
  r27.xyzw = cmp(float4(0,0,0,0) >= r24.xywz);
  r27.xyzw = r27.xyzw ? float4(-1,-1,-1,-1) : float4(-0,-0,-0,-0);
  r28.xz = r21.xy;
  r28.yw = r18.xz;
  r25.xyzw = r25.zwxy * r27.yyxx + r28.zwxy;
  r18.xz = r21.zw;
  r18.xyzw = r26.xyzw * r27.zzww + r18.xyzw;
  r21.xy = r25.zw;
  r21.z = r24.x;
  r26.x = dot(r21.xyz, r21.xyz);
  r25.z = r24.y;
  r26.y = dot(r25.xyz, r25.xyz);
  r27.xy = r18.xy;
  r27.z = r24.w;
  r26.z = dot(r27.xyz, r27.xyz);
  r24.xy = r18.zw;
  r26.w = dot(r24.xyz, r24.xyz);
  r18.xyzw = -r26.xyzw * float4(0.853734732,0.853734732,0.853734732,0.853734732) + float4(1.79284286,1.79284286,1.79284286,1.79284286);
  r17.xzw = r21.xyz * r18.xxx;
  r21.xyz = r25.xyz * r18.yyy;
  r18.xyz = r27.xyz * r18.zzz;
  r24.xyz = r24.xyz * r18.www;
  r25.x = dot(r10.xyz, r10.xyz);
  r25.y = dot(r20.xyz, r20.xyz);
  r25.z = dot(r22.xyz, r22.xyz);
  r25.w = dot(r23.xyz, r23.xyz);
  r25.xyzw = float4(0.600000024,0.600000024,0.600000024,0.600000024) + -r25.xyzw;
  r25.xyzw = max(float4(0,0,0,0), r25.xyzw);
  r25.xyzw = r25.xyzw * r25.xyzw;
  r25.xyzw = r25.xyzw * r25.xyzw;
  r10.x = dot(r10.xyz, r17.xzw);
  r10.y = dot(r20.xyz, r21.xyz);
  r10.z = dot(r22.xyz, r18.xyz);
  r10.w = dot(r23.xyz, r24.xyz);
  r7.w = dot(r25.xyzw, r10.xyzw);
  r7.w = r7.w * 21 + 0.5;
  r7.w = cb0[24].w / r7.w;
  r7.w = r7.w * r7.w + -cb0[24].w;
  r7.w = saturate(r7.w * r9.w);
  r9.w = r7.w * -2 + 3;
  r7.w = r7.w * r7.w;
  r7.w = r9.w * r7.w;
  r7.w = min(1, r7.w);
  r8.w = r8.w * r7.w + -r11.x;
  r19.y = r8.w * r5.w;
  r19.z = 0;
  r10.xyz = float3(0,0,1) + -r19.xyz;
  r8.w = dot(r10.xyz, r10.xyz);
  r8.w = rsqrt(r8.w);
  r10.xyz = r10.xyz * r8.www + -r16.xyz;
  r10.xyz = r11.yyy * r10.xyz + r16.xyz;
  r4.y = t14.Sample(s12_s, r4.zy).y;
  r18.x = r4.y + -r5.x;
  r4.x = t14.Sample(s12_s, r4.xw).y;
  r18.y = r4.x + -r5.x;
  r18.z = 0;
  r4.xyz = float3(0,0,1) + -r18.xyz;
  r4.w = dot(r4.xyz, r4.xyz);
  r4.w = rsqrt(r4.w);
  r17.xzw = r4.xyz * r4.www + -r10.xyz;
  r10.xyz = r1.www * r17.xzw + r10.xyz;
  r5.x = t15.Sample(s13_s, r12.zy).z;
  r5.x = r5.x + -r11.w;
  r18.x = cb0[30].z * r5.x;
  r5.x = t15.Sample(s13_s, r12.xw).z;
  r5.x = r5.x + -r11.w;
  r18.y = cb0[30].z * r5.x;
  r18.z = 0;
  r11.xyw = float3(0,0,1) + -r18.xyz;
  r5.x = dot(r11.xyw, r11.xyw);
  r5.x = rsqrt(r5.x);
  r12.xyz = r11.xyw * r5.xxx + -r10.xyz;
  r10.xyz = r14.xxx * r12.xyz + r10.xyz;
  r12.x = dot(v2.xyz, r10.xyz);
  r12.y = dot(v3.xyz, r10.xyz);
  r12.z = dot(v4.xyz, r10.xyz);
  r8.w = dot(r12.xyz, r12.xyz);
  r8.w = rsqrt(r8.w);
  r10.xyz = r12.xyz * r8.www;
  r10.w = 1;
  r12.x = dot(cb2[39].xyzw, r10.xyzw);
  r12.y = dot(cb2[40].xyzw, r10.xyzw);
  r12.z = dot(cb2[41].xyzw, r10.xyzw);
  r18.xyzw = r10.xyzz * r10.yzzx;
  r19.x = dot(cb2[42].xyzw, r18.xyzw);
  r19.y = dot(cb2[43].xyzw, r18.xyzw);
  r19.z = dot(cb2[44].xyzw, r18.xyzw);
  r8.w = r10.y * r10.y;
  r8.w = r10.x * r10.x + -r8.w;
  r10.xyz = cb2[45].xyz * r8.www + r19.xyz;
  r10.xyz = r12.xyz + r10.xyz;
  r8.w = t7.Sample(s5_s, v1.xy).x;
  r8.w = r8.w * r14.z;
  r12.xyz = cb0[12].xyz * r8.www;
  r10.xyz = r12.xyz * r10.xyz;
  r10.xyz = cb0[7].zzz * r10.xyz;
  r6.xyz = r6.xyz * r15.xyz + r10.xyz;
  r8.w = cmp(cb5[0].x == 1.000000);
  if (r8.w != 0) {
    r8.w = cmp(cb5[0].y == 1.000000);
    r10.xyz = cb5[2].xyz * v3.www;
    r10.xyz = cb5[1].xyz * v2.www + r10.xyz;
    r10.xyz = cb5[3].xyz * v4.www + r10.xyz;
    r10.xyz = cb5[4].xyz + r10.xyz;
    r10.xyz = r8.www ? r10.xyz : r0.xyz;
    r10.xyz = -cb5[6].xyz + r10.xyz;
    r10.yzw = cb5[5].xyz * r10.xyz;
    r8.w = r10.y * 0.25 + 0.75;
    r9.w = cb5[0].z * 0.5 + 0.75;
    r10.x = max(r9.w, r8.w);
    r10.xyzw = t4.Sample(s1_s, r10.xzw).xyzw;
  } else {
    r10.xyzw = float4(1,1,1,1);
  }
  r8.w = saturate(dot(r10.xyzw, cb2[46].xyzw));
  r9.w = cmp(cb0[2].w == 0.000000);
  r10.xy = float2(8,-1);
  while (true) {
    r10.z = cmp(1 < (int)r10.y);
    if (r10.z != 0) break;
    r12.y = (int)r10.y;
    r10.z = r10.x;
    r10.w = -1;
    while (true) {
      r12.z = cmp(1 < (int)r10.w);
      if (r12.z != 0) break;
      r12.x = (int)r10.w;
      r12.zw = r12.xy + r9.xy;
      r14.z = dot(r12.zw, float2(127.099998,311.700012));
      r12.z = dot(r12.zw, float2(269.5,183.300003));
      r15.x = sin(r14.z);
      r15.y = sin(r12.z);
      r12.zw = float2(43758.5469,43758.5469) * r15.xy;
      r12.zw = frac(r12.zw);
      r12.zw = r12.zw * float2(6.28310013,6.28310013) + r8.xx;
      r12.zw = sin(r12.zw);
      r12.zw = r12.zw * float2(0.5,0.5) + float2(0.5,0.5);
      r15.xy = -r12.xy + r8.yz;
      r12.xz = r15.xy + -r12.zw;
      r12.x = dot(r12.xz, r12.xz);
      r12.x = 0.5 * r12.x;
      r12.z = cmp(r12.x < r10.z);
      r10.z = r12.z ? r12.x : r10.z;
      r10.w = (int)r10.w + 1;
    }
    r10.x = r10.z;
    r10.y = (int)r10.y + 1;
  }
  r8.x = cb0[24].x / r10.x;
  r8.x = saturate(1 + -r8.x);
  r8.y = r8.x * r9.z;
  r5.y = r8.x * r5.y + -r8.y;
  r9.x = r5.y * r5.w;
  r5.y = r8.x * r7.w + -r8.y;
  r9.y = r5.y * r5.w;
  r9.z = 0;
  r9.xyz = float3(0,0,1) + -r9.xyz;
  r5.y = dot(r9.xyz, r9.xyz);
  r5.y = rsqrt(r5.y);
  r7.w = r8.y * r5.z;
  r5.w = r7.w * r5.w;
  r2.w = r5.w * cb0[25].y + -r2.w;
  r2.w = saturate(r2.w * r7.x);
  r5.w = r2.w * -2 + 3;
  r2.w = r2.w * r2.w;
  r2.w = r5.w * r2.w;
  r8.xyz = r9.xyz * r5.yyy + -r16.xyz;
  r8.xyz = r2.www * r8.xyz + r16.xyz;
  r4.xyz = r4.xyz * r4.www + -r8.xyz;
  r4.xyz = r1.www * r4.xyz + r8.xyz;
  r5.xyw = r11.xyw * r5.xxx + -r4.xyz;
  r4.xyz = r14.xxx * r5.xyw + r4.xyz;
  r8.x = dot(v2.xyz, r4.xyz);
  r8.y = dot(v3.xyz, r4.xyz);
  r8.z = dot(v4.xyz, r4.xyz);
  r1.w = cb0[32].x * cb0[23].y;
  r1.w = r1.w * r5.z;
  r4.xyz = cb0[31].xyz + -cb0[3].xyz;
  r4.xyz = r1.www * r4.xyz + cb0[3].xyz;
  r2.w = r7.y * r6.w + r2.w;
  r2.w = cb0[25].z * r7.z + r2.w;
  r5.xyw = cb0[31].xyz + -r4.xyz;
  r4.xyz = r2.www * r5.xyw + r4.xyz;
  r5.xyw = t13.Sample(s10_s, v1.xy).xzw;
  r4.w = r5.x * r5.w;
  r3.xyz = r3.xyz * float3(0.25,0.25,0.25) + -r4.xyz;
  r3.xyz = r4.www * r3.xyz + r4.xyz;
  r4.x = r13.x * r13.y;
  r4.x = r11.z * r4.x + r13.w;
  r4.x = saturate(10 * r4.x);
  r4.yzw = cb0[33].xyz + -r3.xyz;
  r3.xyz = r4.xxx * r4.yzw + r3.xyz;
  r4.y = -0.850000024 + r17.y;
  r4.y = r5.z * r4.y + 0.850000024;
  r4.z = 1 + -r14.y;
  r4.y = r4.y + -r4.z;
  r4.z = cb0[34].y + -r4.y;
  r4.y = r14.w * r4.z + r4.y;
  r1.w = 0.5 * r1.w;
  r1.w = saturate(cb0[34].x * r4.y + r1.w);
  r4.y = max(0, cb0[34].z);
  r4.y = min(0.829999983, r4.y);
  r4.y = r4.y + -r1.w;
  r1.w = r2.w * r4.y + r1.w;
  r2.w = r5.y * r3.w;
  r3.w = cb0[34].w + -r1.w;
  r1.w = r2.w * r3.w + r1.w;
  r2.w = cb0[35].x + -r1.w;
  r1.w = r4.x * r2.w + r1.w;
  r2.w = cb0[4].z * r13.z + 1;
  r3.w = 1 + -r1.w;
  r4.x = dot(-r2.xyz, r8.xyz);
  r4.x = r4.x + r4.x;
  r4.xyz = r8.xyz * -r4.xxx + -r2.xyz;
  r5.xyz = cb0[2].xyz * r8.www;
  r4.w = cmp(0 < cb4[2].w);
  if (r4.w != 0) {
    r4.w = dot(r4.xyz, r4.xyz);
    r4.w = rsqrt(r4.w);
    r7.xyz = r4.xyz * r4.www;
    r9.xyz = cb4[0].xyz + -r0.xyz;
    r9.xyz = r9.xyz / r7.xyz;
    r10.xyz = cb4[1].xyz + -r0.xyz;
    r10.xyz = r10.xyz / r7.xyz;
    r11.xyz = cmp(float3(0,0,0) < r7.xyz);
    r9.xyz = r11.xyz ? r9.xyz : r10.xyz;
    r4.w = min(r9.x, r9.y);
    r4.w = min(r4.w, r9.z);
    r9.xyz = -cb4[2].xyz + r0.xyz;
    r7.xyz = r7.xyz * r4.www + r9.xyz;
  } else {
    r7.xyz = r4.xyz;
  }
  r4.w = -r3.w * 0.699999988 + 1.70000005;
  r4.w = r4.w * r3.w;
  r4.w = 6 * r4.w;
  r7.xyzw = t2.SampleLevel(s0_s, r7.xyz, r4.w).xyzw;
  r5.w = -1 + r7.w;
  r5.w = cb4[3].w * r5.w + 1;
  r5.w = log2(r5.w);
  r5.w = cb4[3].y * r5.w;
  r5.w = exp2(r5.w);
  r5.w = cb4[3].x * r5.w;
  r9.xyz = r5.www * r7.xyz;
  r6.w = cmp(cb4[1].w < 0.999989986);
  if (r6.w != 0) {
    r6.w = cmp(0 < cb4[6].w);
    if (r6.w != 0) {
      r6.w = dot(r4.xyz, r4.xyz);
      r6.w = rsqrt(r6.w);
      r10.xyz = r6.www * r4.xyz;
      r11.xyz = cb4[4].xyz + -r0.xyz;
      r11.xyz = r11.xyz / r10.xyz;
      r12.xyz = cb4[5].xyz + -r0.xyz;
      r12.xyz = r12.xyz / r10.xyz;
      r13.xyz = cmp(float3(0,0,0) < r10.xyz);
      r11.xyz = r13.xyz ? r11.xyz : r12.xyz;
      r6.w = min(r11.x, r11.y);
      r6.w = min(r6.w, r11.z);
      r0.xyz = -cb4[6].xyz + r0.xyz;
      r4.xyz = r10.xyz * r6.www + r0.xyz;
    }
    r4.xyzw = t3.SampleLevel(s0_s, r4.xyz, r4.w).xyzw;
    r0.x = -1 + r4.w;
    r0.x = cb4[7].w * r0.x + 1;
    r0.x = log2(r0.x);
    r0.x = cb4[7].y * r0.x;
    r0.x = exp2(r0.x);
    r0.x = cb4[7].x * r0.x;
    r0.xyz = r0.xxx * r4.xyz;
    r4.xyz = r5.www * r7.xyz + -r0.xyz;
    r9.xyz = cb4[1].www * r4.xyz + r0.xyz;
  }
  r0.xyz = r9.xyz * r2.www;
  r2.w = dot(r8.xyz, r8.xyz);
  r2.w = rsqrt(r2.w);
  r4.xyz = r8.xyz * r2.www;
  r2.w = max(r3.x, r3.y);
  r2.w = max(r2.w, r3.z);
  r2.w = 1 + -r2.w;
  r1.xyz = r1.xyz * r0.www + cb2[0].xyz;
  r0.w = dot(r1.xyz, r1.xyz);
  r0.w = max(0.00100000005, r0.w);
  r0.w = rsqrt(r0.w);
  r1.xyz = r1.xyz * r0.www;
  r0.w = dot(r4.xyz, r2.xyz);
  r4.w = saturate(dot(r4.xyz, cb2[0].xyz));
  r5.w = saturate(dot(r4.xyz, r1.xyz));
  r1.x = saturate(dot(cb2[0].xyz, r1.xyz));
  r1.y = r3.w * r3.w;
  r1.y = max(0.00200000009, r1.y);
  r1.z = 1 + -r1.y;
  r3.w = abs(r0.w) * r1.z + r1.y;
  r1.z = r4.w * r1.z + r1.y;
  r1.z = r1.z * abs(r0.w);
  r1.z = r4.w * r3.w + r1.z;
  r1.z = 9.99999975e-006 + r1.z;
  r1.z = 0.5 / r1.z;
  r3.w = r1.y * r1.y;
  r6.w = r5.w * r3.w + -r5.w;
  r5.w = r6.w * r5.w + 1;
  r3.w = 0.318309873 * r3.w;
  r5.w = r5.w * r5.w + 1.00000001e-007;
  r3.w = r3.w / r5.w;
  r1.z = r3.w * r1.z;
  r1.z = 3.14159274 * r1.z;
  r1.z = r1.z * r4.w;
  r1.z = max(0, r1.z);
  r1.y = r1.y * r1.y + 1;
  r1.y = 1 / r1.y;
  r3.w = dot(r3.xyz, r3.xyz);
  r3.w = cmp(r3.w != 0.000000);
  r3.w = r3.w ? 1.000000 : 0;
  r1.z = r3.w * r1.z;
  r2.w = 1 + -r2.w;
  r2.w = saturate(r2.w + r1.w);
  r7.xyz = r1.zzz * r5.xyz;
  r1.x = 1 + -r1.x;
  r1.z = r1.x * r1.x;
  r1.z = r1.z * r1.z;
  r1.x = r1.z * r1.x;
  r8.xyz = float3(1,1,1) + -r3.xyz;
  r8.xyz = r8.xyz * r1.xxx + r3.xyz;
  r0.w = 1 + -abs(r0.w);
  r1.x = r0.w * r0.w;
  r1.x = r1.x * r1.x;
  r0.xyzw = r1.yyyx * r0.xyzw;
  r1.xyz = r2.www + -r3.xyz;
  r1.xyz = r0.www * r1.xyz + r3.xyz;
  r0.xyz = r1.xyz * r0.xyz;
  r0.xyz = r7.xyz * r8.xyz + r0.xyz;
  r0.w = dot(cb2[0].xyz, cb2[0].xyz);
  r0.w = rsqrt(r0.w);
  r1.xyz = cb2[0].xyz * r0.www + r2.xyz;
  r0.w = dot(r1.xyz, r1.xyz);
  r0.w = rsqrt(r0.w);
  r1.xyz = r1.xyz * r0.www;
  r0.w = dot(r1.xyz, r4.xyz);
  r0.w = max(0, r0.w);
  r1.x = 14.4269505 * cb0[35].y;
  r1.x = exp2(r1.x);
  r0.w = log2(r0.w);
  r0.w = r1.x * r0.w;
  r0.w = exp2(r0.w);
  r1.xyz = r9.www ? float3(0,0,0) : r5.xyz;
  r1.w = cb0[34].x * r1.w;
  r1.xyz = r1.xyz * r1.www;
  r0.xyz = r0.www * r1.xyz + r0.xyz;
  o0.xyz = r0.xyz + r6.xyz;
  o0.w = 1;
  return;
}