using System;using System.Collections.Generic;using System.IO;using System.Management.Automation;using System.Web.Script.Serialization;
namespace TGLL {
 public static class StoredJson {
  public static object Read(string path){return Parse(File.ReadAllText(path,System.Text.Encoding.UTF8));}
  public static object Parse(string text){var reader=new JavaScriptSerializer{MaxJsonLength=Int32.MaxValue,RecursionLimit=128};return ConvertValue(reader.DeserializeObject(text));}
  static object ConvertValue(object value){
   var map=value as IDictionary<string,object>;if(map!=null){var result=new PSObject();foreach(var pair in map)result.Properties.Add(new PSNoteProperty(pair.Key,ConvertValue(pair.Value)));return result;}
   var list=value as object[];if(list!=null){var result=new object[list.Length];for(int n=0;n<list.Length;n++)result[n]=ConvertValue(list[n]);return result;}
   return value;
  }
 }
}
