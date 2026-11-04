// Bounded C# port of Meta SAM parser's one_bit mask codec.
// Copyright (c) Meta Platforms, Inc. and affiliates. All Rights Reserved.
// Modified for Unity. Distributed under Docs/ThirdParty/MetaSam-LICENSE.txt.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace AncestorWalk
{
    public sealed class SamMask
    {
        public string Id;public int Left,Top,Width,Height;public byte[] Alpha;
        static readonly string Alphabet=new string(Enumerable.Range(33,94).Select(i=>(char)i).Where(c=>!"\"\\,;<>|".Contains(c)).ToArray());
        static int Digit(char c){int i=Alphabet.IndexOf(c);if(i<0||i>=85)throw new InvalidDataException("Invalid SAM mask alphabet.");return i;}
        static byte[] Unpack(string text)
        {
            if(text.Length<5)throw new InvalidDataException("Truncated SAM mask.");long length=0;for(int i=0;i<5;i++)length=length*85+Digit(text[i]);
            if(length<4||length>8*1024*1024)throw new InvalidDataException("SAM mask exceeds supported size.");int n=(int)length,remainder=n%4;
            if(text.Length!=5+n/4*5+(remainder==0?0:remainder+1))throw new InvalidDataException("SAM mask length mismatch.");
            var output=new byte[n];int source=5;
            for(int at=0;at<n;at+=4)
            {
                int count=Math.Min(4,n-at);ulong value=0;for(int j=0;j<5;j++)value=value*85+(ulong)(j<count+1?Digit(text[source+j]):84);
                if(value>uint.MaxValue)throw new InvalidDataException("SAM mask group overflow.");for(int j=0;j<count;j++)output[at+j]=(byte)(value>>(24-j*8));source+=count==4?5:count+1;
            }
            if(Pack(output)!=text)throw new InvalidDataException("Noncanonical SAM mask.");return output;
        }
        static string Pack(byte[] bytes)
        {
            var output=new StringBuilder();var prefix=new char[5];int n=bytes.Length;for(int j=4;j>=0;j--){prefix[j]=Alphabet[n%85];n/=85;}output.Append(prefix);
            for(int at=0;at<bytes.Length;at+=4)
            {int count=Math.Min(4,bytes.Length-at);uint value=0;for(int j=0;j<4;j++)value=(value<<8)|(j<count?bytes[at+j]:0u);var digits=new char[5];for(int j=4;j>=0;j--){digits[j]=Alphabet[(int)(value%85)];value/=85;}output.Append(digits,0,count==4?5:count+1);}return output.ToString();
        }
        sealed class Decoder
        {
            readonly byte[] input;int at;uint low,range=uint.MaxValue,code,scaled;
            public Decoder(byte[] bytes){input=bytes;for(int i=0;i<4;i++)code=(code<<8)|Read();}
            uint Read(){if(at>=input.Length)throw new InvalidDataException("Incomplete SAM mask coding state.");return input[at++];}
            public uint Frequency(uint total){scaled=range/total;if(scaled==0)throw new InvalidDataException("Invalid SAM coding state.");return Math.Min(total-1,unchecked(code-low)/scaled);}
            public void Decode(uint cumulative,uint frequency)
            {
                low=unchecked(low+scaled*cumulative);range=scaled*frequency;
                while(true){if((low^unchecked(low+range))<(1u<<24)){}else if(range<(1u<<16))range=unchecked(0u-low)&65535;else break;code=(code<<8)|Read();low<<=8;range<<=8;}
            }
        }
        sealed class Encoder
        {
            uint low,range=uint.MaxValue;readonly List<byte> output=new();
            public void Encode(uint cumulative,uint frequency,uint total)
            {
                uint scaled=range/total;low=unchecked(low+scaled*cumulative);range=scaled*frequency;
                while(true){if((low^unchecked(low+range))<(1u<<24)){}else if(range<(1u<<16))range=unchecked(0u-low)&65535;else break;output.Add((byte)(low>>24));low<<=8;range<<=8;}
            }
            public byte[] Finish(){for(int i=0;i<4;i++){output.Add((byte)(low>>24));low<<=8;}return output.ToArray();}
        }
        static int Context(byte[] padded,int p,int width)
        {int a=p-width,b=p-2*width;return padded[p-1]|padded[p-2]<<1|padded[a-2]<<2|padded[a-1]<<3|padded[a]<<4|padded[a+1]<<5|padded[a+2]<<6|padded[b-2]<<7|padded[b-1]<<8|padded[b]<<9|padded[b+1]<<10|padded[b+2]<<11;}
        static void Update(ushort[] counts,int index,int bit){counts[index+bit]+=14;if(counts[index]+counts[index+1]>=4096){counts[index]=(ushort)Math.Max(1,counts[index]>>1);counts[index+1]=(ushort)Math.Max(1,counts[index+1]>>1);}}
        public static byte[] Decode(string payload,int width,int height)
        {
            if(width<1||height<1||width>1536||height>1536||payload==null||!payload.StartsWith("!"))throw new InvalidDataException("Expected bounded one_bit SAM mask.");
            var decoder=new Decoder(Unpack(payload.Substring(1)));var encoder=new Encoder();int stride=width+4;var padded=new byte[stride*(height+2)];var counts=Enumerable.Repeat((ushort)1,8192).ToArray();var output=new byte[width*height];
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {
                int p=(y+2)*stride+x+2,index=Context(padded,p,stride)*2;uint zero=counts[index],one=counts[index+1];int bit=decoder.Frequency(zero+one)<zero?0:1;
                decoder.Decode(bit==0?0:zero,bit==0?zero:one);encoder.Encode(bit==0?0:zero,bit==0?zero:one,zero+one);padded[p]=(byte)bit;output[y*width+x]=(byte)(bit*255);Update(counts,index,bit);
            }
            if("!"+Pack(encoder.Finish())!=payload)throw new InvalidDataException("SAM mask has invalid finalization.");return output;
        }
        public static List<SamMask> Parse(JObject response,int width,int height)
        {
            if((string)response["status"]!="completed"||response["output"] is not JArray output)throw new InvalidDataException("SAM did not complete.");
            string text=string.Concat(output.OfType<JObject>().Where(i=>(string)i["type"]=="message"&&(string)i["role"]=="assistant").SelectMany(i=>(i["content"] as JArray??new JArray()).OfType<JObject>()).Where(p=>(string)p["type"]=="output_text").Select(p=>(string)p["text"]??""));
            if(text.Length>24*1024*1024)throw new InvalidDataException("SAM output too large.");
            string pattern=@"(?<id>\d+)<\|box;(?<box>[^|]+)\|><\|mask;x=0;y=0;(?:c=[^;]+;)?data=(?<h>\d+),(?<w>\d+),(?<data>[^|]+)\|>";
            var matches=Regex.Matches(text,pattern);if(matches.Count>16||matches.Count!=Regex.Matches(text,@"<\|mask;").Count)throw new InvalidDataException("Invalid or excessive SAM matches.");
            var result=new List<SamMask>();foreach(Match match in matches)
            {
                var fields=match.Groups["box"].Value.Split(';').Select(s=>s.Split('=')).ToDictionary(a=>a[0],a=>a[1]);int Read(string name)=>int.Parse(fields[name]);
                int left=Read("x1"),top=Read("y1"),right=Read("x2")+1,bottom=Read("y2")+1,w=int.Parse(match.Groups["w"].Value),h=int.Parse(match.Groups["h"].Value);
                if(Read("w")!=width||Read("h")!=height||left<0||top<0||right>width||bottom>height||w!=right-left||h!=bottom-top)throw new InvalidDataException("SAM mask dimensions differ from source image.");
                result.Add(new SamMask{Id=match.Groups["id"].Value,Left=left,Top=top,Width=w,Height=h,Alpha=Decode(match.Groups["data"].Value,w,h)});
            }
            return result.OrderBy(m=>m.Left).ThenBy(m=>m.Top).ToList();
        }
    }
}
