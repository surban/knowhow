// debugging console
//if (!("console" in window)) {
//    window.console = {};
//    window.console["log"] = function () { };
//}


// parameters
var metadata = JSON.parse($("#metadata").html());
var preloading = (document.location.href.indexOf("?preload") != -1);


function detectIE() {
    var ua = window.navigator.userAgent;
    var msie = ua.indexOf('MSIE ');
    var trident = ua.indexOf('Trident/');

    if (msie > 0) {
        // IE 10 or older => return version number
        return parseInt(ua.substring(msie + 5, ua.indexOf('.', msie)), 10);
    }

    if (trident > 0) {
        // IE 11 (or newer) => return version number
        var rv = ua.indexOf('rv:');
        return parseInt(ua.substring(rv + 3, ua.indexOf('.', rv)), 10);
    }

    // other browser
    return false;
}

function preloaderReady() {
    // called by page loaded in iframe when math rendering is completed
    updated_doc = document.getElementById('preloader').contentWindow.document;
    document.getElementById('content').innerHTML =
        updated_doc.getElementById('content').innerHTML;
}

$(function () {
    if (!preloading) {
        // we are top-level page
        
        // display content after math has been rendered
        MathJax.Hub.Queue(
            function () {
                // style must be removed or Chrome ignores class
                document.getElementById("content").removeAttribute("style");
                document.getElementById("content").className = "content";
            });

        // register with server for change notifications
        var rpc = $.connection.signalHub;
        rpc.client.fileChanged = function (path, mtime) {
            if (mtime != metadata.SourceMTime) {
                console.log("source updated (original=" + metadata.SourceMTime + ", new=" + mtime + ")");
                document.getElementById('preloader').src = document.location.href + "?preload";
                metadata.SourceMTime = mtime;
            }
        }
        $.connection.hub.start().done(function () {
            console.log("registering with server to watch " + metadata.RequestPath);
            rpc.server.watchFile(metadata.RequestPath);
        });
    }
    else {
        // we are being loaded in an iframe for prerendering
        // tell parent when we are rendered
        MathJax.Hub.Queue(parent.preloaderReady);
    }
})
