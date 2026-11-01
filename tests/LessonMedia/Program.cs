using HackAcad.Web.Services;

var checks=0;
void Check(bool valid,string name){if(!valid)throw new Exception("FAIL: "+name);checks++;Console.WriteLine("PASS: "+name);}
const string id="X3DVaMnl5n8";
foreach(var url in new[]{"https://www.youtube.com/watch?feature=share&v="+id,"https://youtu.be/"+id+"?si=test","https://m.youtube.com/watch?v="+id,"https://www.youtube.com/shorts/"+id,"https://youtube.com/live/"+id,"https://www.youtube-nocookie.com/embed/"+id})
    Check(LessonVideo.EmbedUrl(url)=="https://www.youtube-nocookie.com/embed/"+id+"?rel=0&hl=en","supported YouTube link normalizes to the trusted English-language player");
foreach(var url in new[]{"https://youtube.com.evil.test/watch?v="+id,"https://evil.test/youtube.com/watch?v="+id,"https://youtube.com@evil.test/watch?v="+id,"https://evil.test/video","javascript:alert(1)","data:text/html,test","//youtube.com/watch?v="+id,"https://youtube.com/embed/invalid","https://www.youtube.com/watch?v="+id+"%22onload=alert(1)"})
    Check(LessonVideo.EmbedUrl(url)==null,"untrusted or malformed URL cannot become an iframe");
Check(LessonVideo.DirectUrl("https://cdn.example.test/lesson.mp4?download=1")!=null && LessonVideo.IsSupported("https://cdn.example.test/lesson.webm"),"direct HTTP video files remain supported");
Check(LessonVideo.DirectUrl("javascript:video.mp4")==null && LessonVideo.IsSupported(null),"unsafe schemes are refused and video stays optional");
var html=MarkdownService.RenderHtml("## Concept\n\n![Encoding comparison](/images/lessons/encoding-encryption-hashing.svg)\n\n| Operation | Key? |\n| --- | --- |\n| `Base64` | No |\n| Encryption | Yes |\n\n1. Inspect the sample.\n2. Explain your result.\n\n```text\n<script>literal code</script>\n```");
Check(html.Contains("<img src=\"/images/lessons/encoding-encryption-hashing.svg\"")&&html.Contains("alt=\"Encoding comparison\"")&&html.Contains("loading=\"lazy\""),"teaching figure has escaped alt text and lazy loading");
Check(html.Contains("<table>")&&html.Contains("<th scope=\"col\">Operation</th>")&&html.Contains("<td><code>Base64</code></td>"),"comparison table retains column headers and inline code");
Check(html.Contains("<ol>")&&html.Contains("<li>Explain your result.</li>"),"practice steps render as a real ordered list");
Check(!html.Contains("<script>")&&html.Contains("&lt;script&gt;literal code&lt;/script&gt;"),"code samples stay literal and cannot run scripts");
foreach(var url in new[]{"javascript:alert(1)","data:image/svg+xml,unsafe","//evil.test/image.svg","/images/../private.svg","https://evil.test/tracker.svg"})
    Check(!MarkdownService.RenderHtml($"![sample]({url})").Contains("<img "),"unsupported image source cannot create an image element");
Check(!MarkdownService.RenderHtml("![\" onerror=\"bad](/images/lessons/ssh-triage.svg)").Contains(" onerror=\""),"image alt text cannot inject an attribute");
Check(!MarkdownService.RenderHtml("| A | B |\n| --- | --- |\n| <img onerror=bad> | [run](javascript:bad) |").Contains("<img"),"table cells remain escaped");
Check(MarkdownService.RenderHtml("![caption](/images/lessons/ssh-triage.svg)\n\nAfter image").Contains("<p>After image</p>"),"text after an image remains a separate paragraph");
Console.WriteLine($"ALL {checks} LESSON MEDIA CHECKS PASSED.");
