using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace AncestorWalk
{
    public sealed class MediaLink
    {public string PersonId,Path,Title,Date,Description;public bool IsPrimary;}
    public static class GedcomMedia
    {
        sealed class Media {public string Id;public readonly List<MediaLink> Files=new();public string Title,Date,Description;}
        sealed class Association {public string Person,Reference;public Media Media;public bool IsPrimary;}
        static readonly Regex Line=new(@"^(\d+)\s+(?:(@[^@\s]+@)\s+)?(\S+)(?:\s+(.*))?$");
        public static List<MediaLink> Parse(string text)
        {
            var records=new Dictionary<string,Media>();var links=new List<Association>();Association association=null;
            string person=null;Media media=null;MediaLink file=null;int baseLevel=0;
            using var reader=new StringReader(text.TrimStart('\uFEFF'));string raw;
            while((raw=reader.ReadLine())!=null)
            {
                var m=Line.Match(raw);if(!m.Success)continue;int level=int.Parse(m.Groups[1].Value);string id=m.Groups[2].Value,tag=m.Groups[3].Value,value=m.Groups[4].Value.Trim();
                if(level==0){person=tag=="INDI"?id:null;media=null;file=null;association=null;if(tag=="OBJE"){media=new Media{Id=id};records[id]=media;baseLevel=0;}continue;}
                if(level<=1)association=null;
                if(media!=null&&level<=baseLevel){media=null;file=null;}
                if(person!=null&&level==1&&tag=="OBJE")
                {
                    association=new Association{Person=person};links.Add(association);
                    if(value.StartsWith("@")&&value.EndsWith("@")){association.Reference=value;media=null;file=null;}
                    else{media=new Media();association.Media=media;baseLevel=level;file=null;}continue;
                }
                if(association!=null&&level==2&&tag=="_PRIM")association.IsPrimary=value.Equals("Y",StringComparison.OrdinalIgnoreCase);
                if(media==null)continue;
                if(tag=="FILE"){file=new MediaLink{Path=value};media.Files.Add(file);}
                else if(tag=="TITL"){media.Title=value;if(file!=null)file.Title=value;}
                else if(tag=="DATE"){media.Date=value;if(file!=null)file.Date=value;}
                else if(tag=="NOTE"){media.Description=value;if(file!=null)file.Description=value;}
            }
            var result=new List<MediaLink>();foreach(var link in links)
            {
                var selected=link.Media;if(selected==null&&!records.TryGetValue(link.Reference,out selected)){result.Add(new MediaLink{PersonId=link.Person,Path=link.Reference,IsPrimary=link.IsPrimary});continue;}
                foreach(var f in selected.Files)result.Add(new MediaLink{PersonId=link.Person,Path=f.Path,Title=f.Title??selected.Title,Date=f.Date??selected.Date,Description=f.Description??selected.Description,IsPrimary=link.IsPrimary});
            }
            return result;
        }
    }
    public static class PhotoHeader
    {
        // Bound native decode allocations before asking Unity to decode an image.
        public static void Read(byte[] data,out int width,out int height,out int orientation)
        {
            width=height=0;orientation=1;if(data==null||data.Length<24||data.Length>32*1024*1024)throw new InvalidDataException("Photo is empty or larger than 32 MB.");
            if(data[0]==137&&data[1]==80&&data[2]==78&&data[3]==71&&data[4]==13&&data[5]==10&&data[6]==26&&data[7]==10)
            {width=Big32(data,16);height=Big32(data,20);}
            else if(data[0]==255&&data[1]==216)
            {
                int pos=2;while(pos+4<=data.Length)
                {
                    if(data[pos++]!=255)break;while(pos<data.Length&&data[pos]==255)pos++;if(pos>=data.Length)break;int marker=data[pos++];
                    if(marker==217||marker==218)break;if(marker==1||marker>=208&&marker<=215)continue;
                    if(pos+2>data.Length)break;int size=data[pos]*256+data[pos+1];if(size<2||pos+size>data.Length)break;
                    if(marker==225&&size>=16&&data[pos+2]==69&&data[pos+3]==120&&data[pos+4]==105&&data[pos+5]==102)
                    {
                        try{int t=pos+8;bool little=data[t]==73;int U16(int at)=>little?data[at]+data[at+1]*256:data[at]*256+data[at+1];
                            int U32(int at)=>little?data[at]+(data[at+1]<<8)+(data[at+2]<<16)+(data[at+3]<<24):Big32(data,at);
                            int ifd=t+U32(t+4);if(ifd>=t&&ifd+2<pos+size){int count=U16(ifd);for(int i=0;i<count&&ifd+2+(i+1)*12<=pos+size;i++){int at=ifd+2+i*12;if(U16(at)==274&&U16(at+2)==3&&U32(at+4)==1){int value=U16(at+8);if(value>=1&&value<=8)orientation=value;break;}}}}
                        catch(IndexOutOfRangeException){}
                    }
                    if((marker>=192&&marker<=195)||(marker>=197&&marker<=199)||(marker>=201&&marker<=203)||(marker>=205&&marker<=207))
                    {if(size<8)break;height=data[pos+3]*256+data[pos+4];width=data[pos+5]*256+data[pos+6];}
                    pos+=size;
                }
            }
            if(width<1||height<1||width>8192||height>8192||(long)width*height>16_777_216)throw new InvalidDataException("Photo must be a PNG/JPEG of at most 16 megapixels and 8192 pixels per side.");
        }
        static int Big32(byte[] b,int i)=>(b[i]<<24)|(b[i+1]<<16)|(b[i+2]<<8)|b[i+3];
    }
}
