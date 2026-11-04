using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
namespace AncestorWalk
{
    [Serializable] public sealed class SceneReference
    {
        public string sha256,place;public int year;public float horizonLift;
        public string kind,file;
        public bool IsPhoto=>kind=="photo";
        public string ContentType=>IsPhoto&&file.EndsWith(".jpg",StringComparison.Ordinal)?"image/jpeg":"image/png";
        // JsonUtility writes null inline classes as empty objects. Recognize only that
        // exact sentinel; partially populated or malformed references remain invalid.
        public static SceneReference Optional(SceneReference value)=>value!=null&&string.IsNullOrEmpty(value.sha256)&&string.IsNullOrEmpty(value.place)&&string.IsNullOrEmpty(value.kind)&&string.IsNullOrEmpty(value.file)&&value.year==0&&value.horizonLift==0?null:value;
        public static SceneReference Copy(SceneReference value){value=Optional(value);return value==null?null:new SceneReference{sha256=value.sha256,place=value.place,year=value.year,horizonLift=value.horizonLift,kind=value.kind,file=value.file};}
        public static bool Same(SceneReference a,SceneReference b){a=Optional(a);b=Optional(b);return a==null?b==null:b!=null&&a.sha256==b.sha256&&a.year==b.year&&ResidenceTimeline.Same(a.place,b.place)&&Math.Abs(a.horizonLift-b.horizonLift)<.001f&&(a.kind??"")== (b.kind??"")&&(a.file??"")== (b.file??"");}
        public static void Validate(SceneReference value,string place)
        {
            value=Optional(value);if(value==null)return;
            if(value.IsPhoto?(value.file!=value.sha256+".png"&&value.file!=value.sha256+".jpg"):!string.IsNullOrEmpty(value.kind)||!string.IsNullOrEmpty(value.file))throw new InvalidDataException("Invalid scene photo identity.");
            if(!Regex.IsMatch(value.sha256??"",@"\A[a-f0-9]{64}\z")||value.year<1||value.year>9999||string.IsNullOrWhiteSpace(value.place)||value.place.Length>ResidenceTimeline.MaximumLocationLength||!ResidenceTimeline.Same(value.place,place)||float.IsNaN(value.horizonLift)||float.IsInfinity(value.horizonLift)||Math.Abs(value.horizonLift)>25)
                throw new InvalidDataException("Invalid reference panorama identity or location.");
        }
        public string Route(string tree,string personKey)=>(IsPhoto?"/photo-references/":"/references/")+tree+"/"+personKey+"/"+year+"/"+sha256;
        public string ImagePath(string directory,string tree,string person)=>IsPhoto?Path.Combine(Path.GetDirectoryName(directory),"Photos",file):Path.Combine(directory,"people",tree,PanoramaCache.PersonKey(person),"years",year.ToString(),sha256+".png");
        public static SceneReference FromPhoto(PhotoRecord photo,int year,string place)
        {var result=new SceneReference{kind="photo",file=photo.File,sha256=photo.Id,year=year,place=ResidenceTimeline.Normalize(place),horizonLift=12};Validate(result,place);return result;}
        public static SceneReference FromPanorama(PanoramaRecord record)
        {var result=new SceneReference{sha256=record.sha256,year=record.year,place=record.place,horizonLift=record.horizonLift};Validate(result,record.place);return result;}
        public static void ValidatePhotoBase(SceneReference photo,SceneReference basis,int year,string place)
        {basis=Optional(basis);if(basis==null)return;Validate(basis,place);Validate(photo,place);if(basis.IsPhoto||photo?.IsPhoto!=true||basis.year!=year||photo.year!=year)throw new InvalidDataException("Photo-edit base must match the exact year and location.");}
        public static PanoramaRecord ExactSceneAt(string directory,string tree,string person,int year,string place)
            =>PanoramaCache.YearRecordsAt(directory,tree,person).FirstOrDefault(r=>r.year==year&&ResidenceTimeline.Same(r.place,place));
        // Uses validated record stamps; only the selected PNG is subsequently streamed.
        public static SceneReference NearestAt(string directory,string tree,string person,int year,string place,int[] compatibleYears=null)
        {
            var record=PanoramaCache.YearRecordsAt(directory,tree,person)
                .Where(r=>(compatibleYears==null||Array.IndexOf(compatibleYears,r.year)>=0)&&(ResidenceTimeline.Same(r.place,place)||compatibleYears!=null&&string.IsNullOrWhiteSpace(r.place)))
                .OrderBy(r=>Math.Abs(r.year-year)).ThenBy(r=>r.year).FirstOrDefault();
            if(record==null)return null;
            var result=new SceneReference{sha256=record.sha256,year=record.year,place=string.IsNullOrWhiteSpace(record.place)?place:record.place,horizonLift=record.horizonLift};Validate(result,place);return result;
        }
    }
}
