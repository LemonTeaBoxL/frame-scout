using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
namespace FrameScout {
 public class NounSource {public string Episode,Quote;public int Line;}
 public static class ScriptEvidence {
  public static void Enrich(List<Noun> nouns,string script) {string[] lines=script.Replace("\r\n","\n").Split('\n');var episodes=new string[lines.Length];string episode="";for(int i=0;i<lines.Length;i++){if(Regex.IsMatch(lines[i],@"^\s*(?:#+\s*)?第\s*[零〇一二两三四五六七八九十百\d]+\s*集"))episode=SubtitleIO.Episode(lines[i]);episodes[i]=episode;}foreach(var noun in nouns) {noun.Sources=new List<NounSource>();var seen=new HashSet<string>();for(int i=0;i<lines.Length;i++) {int at=lines[i].IndexOf(noun.Canonical,StringComparison.Ordinal);if(at<0||!seen.Add(episodes[i]))continue;int start=Math.Max(0,at-65);string quote=lines[i].Substring(start,Math.Min(190,lines[i].Length-start));noun.Sources.Add(new NounSource {Episode=episodes[i],Line=i+1,Quote=(start>0?"…":"")+quote});}}}
  public static NounSource Best(Noun noun,string episode) {var sources=noun.Sources??new List<NounSource>();return sources.FirstOrDefault(x=>x.Episode==episode)??sources.FirstOrDefault(x=>string.IsNullOrEmpty(x.Episode))??sources.FirstOrDefault();}
  public static string Text(Noun noun,string episode) {var source=Best(noun,episode);return source==null?"人工名词表："+noun.Canonical+"（剧本中未定位到原句）":(string.IsNullOrEmpty(source.Episode)?"剧本人物表 / 通用部分":"第 "+source.Episode+" 集")+" · 第 "+source.Line+" 行："+source.Quote;}
  public static void Attach(List<SubtitleIssue> issues,List<Noun> nouns,string file) {string episode=SubtitleIO.Episode(file);foreach(var issue in issues) {var noun=nouns.FirstOrDefault(n=>n.Canonical==issue.SuggestedTerm||(n.Aliases??"").Split('、').Contains(issue.SuggestedTerm));if(noun==null){issue.Evidence="未在名词表找到出处";continue;}issue.Evidence=Text(noun,episode);var source=Best(noun,episode);if(source!=null&&!string.IsNullOrEmpty(source.Episode)&&source.Episode!=episode) {issue.State=2;issue.Reason+="；出处来自其他集，需人工确认";}}}
 }
}
