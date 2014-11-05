// debugging console
if (!("console" in window)) {
    window.console = {};
    window.console["log"] = function () { };
}

// parameters
var src_name = "%(src_name)s";
var preloading = (document.location.href.indexOf("?preload") != -1);

function watchForChange() {
    var watch_ws = new WebSocket("ws://" + document.location.host + "/watch/" + src_name);
    watch_ws.onmessage =
        function (evt) {
            mtime = parseInt(evt.data, 10);
            mmd_modified = parseInt(document.getElementById("mmd_modified").innerHTML, 10);
            if (mtime != mmd_modified) {
                console.log("source updated (original=" + mmd_modified + ", new=" + mtime + ")");
                updateContent();
            }
        };
}

function childReady() {
    // called by page loaded in iframe when math rendering is completed
    updated_doc = document.getElementById('preloader').contentWindow.document;

    if (updated_doc.getElementById('error') == null) {
        document.getElementById('overlay').style.display = "none";
        document.getElementById('content').innerHTML =
            updated_doc.getElementById('content').innerHTML;
    }
    else {
        document.getElementById('overlay_content').innerHTML =
            updated_doc.getElementById('error').innerHTML;
        document.getElementById('overlay').style.display = "inline";
    }

}

function updateContent() {
    document.getElementById('preloader').src = document.location.href + "?preload";
}

if (!preloading) {
    // we are top-level page, display content after math has been rendered
    MathJax.Hub.Queue(
        function () {
            // style must be removed or Chrome ignores class
            document.getElementById("content").removeAttribute("style");
            document.getElementById("content").className = "content";
        });

    // watch for source file updates
    //watchForChange();
}
else {
    // we are being loaded in an iframe for prerendering, tell parent when we are rendered
    MathJax.Hub.Queue(parent.childReady);
}
