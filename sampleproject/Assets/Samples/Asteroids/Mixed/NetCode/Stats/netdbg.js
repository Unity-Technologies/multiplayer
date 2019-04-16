window.addEventListener("load", initNetDbg);

function initNetDbg() {
	g_debugger = new NetDbg();
}

function NetDbg() {
	this.canvas = document.getElementById("canvas");
	this.ctx = this.canvas.getContext("2d");

	this.selection = -1;
	this.offsetX = -1;
	this.canvas.addEventListener("mousedown", this.startDrag.bind(this));
	this.dragEvt = this.updateDrag.bind(this);
	this.dragStopEvt = this.stopDrag.bind(this);

	document.getElementById("liveUpdate").checked = true;

	/*var loader = new XMLHttpRequest();
	loader.dbg = this;
	loader.addEventListener("load", function(){this.dbg.loadContent(this.response);});
	loader.open("GET", "snapshots.json");
	loader.responseType = "json";
	loader.send();*/

	this.content = {};
	this.updateNames("Destroy");
	this.content.snapshots = [];
	//this.connect("localhost:8787");

	this.pendingPresent = 0;
	this.pendingStats = 0;
	this.invalidate();
}

NetDbg.prototype.SnapshotWidth = 10;
NetDbg.prototype.SnapshotMargin = 2;
NetDbg.prototype.Colors = ["red", "green", "blue", "yellow"];


NetDbg.prototype.updateNames = function(nameList) {
	this.content.names = nameList.split(";");
	this.content.total = new Array(this.content.names.length*2);
	var legend = document.getElementById("legendOverlay");
	while (legend.firstChild)
		legend.removeChild(legend.firstChild);
	for (var i = 0; i < this.content.names.length; ++i) {
		this.content.total[i*2] = 0;
		this.content.total[i*2 + 1] = 0;
		var line = document.createElement("div");
		line.style.color = "white";
		line.style.padding = "2px";
		line.style.margin = "2px";
		line.style.borderWidth = "1px";
		line.style.borderColor = this.Colors[i%this.Colors.length];
		line.style.borderStyle = "solid";
		line.appendChild(document.createTextNode(this.content.names[i]));
		legend.appendChild(line);
	}
}

NetDbg.prototype.invalidateLegendStats = function() {
	if (this.pendingStats)
		return;
	this.pendingStats = setTimeout(this.updateLegendStats.bind(this), 100);
}

NetDbg.prototype.updateLegendStats = function() {
	this.pendingStats = 0;
	var legend = document.getElementById("legendOverlay");
	var items = legend.children;
	for (var i = 0; i < this.content.names.length; ++i) {
		if (this.content.total[i*2] > 0) {
			var avgFrame = Math.round(this.content.total[i*2] / this.content.snapshots.length);
			var avgEnt = Math.round(this.content.total[i*2] / this.content.total[i*2 + 1]);
			items[i].firstChild.nodeValue = this.content.names[i] + ": " + avgFrame + " bits/frame, " + avgEnt + " bits/entity";
		}
	}
}

NetDbg.prototype.connect = function(host) {
	document.getElementById('connectDlg').className = "NetDbgConnecting";
	// Clear the existing data
	this.content = {};
	this.updateNames("Destroy");
	this.content.snapshots = [];
	this.selection = -1;
	this.offsetX = -1;
	document.getElementById("liveUpdate").checked = true;
	// Connect to unity
	this.ws = new WebSocket("ws://" + host);
	this.ws.binaryType = "arraybuffer";
	this.ws.addEventListener("message", this.wsReceive.bind(this));
	this.ws.addEventListener("open", this.wsOpen.bind(this));
	this.ws.addEventListener("close", this.wsClose.bind(this));
	//this.ws.addEventListener("error", this.wsClose.bind(this));
}
NetDbg.prototype.disconnect = function() {
	this.ws.close();
	document.getElementById('connectDlg').className = "NetDbgDisconnected";
}

NetDbg.prototype.wsOpen = function(evt) {
	document.getElementById('connectDlg').className = "NetDbgConnected";
}

NetDbg.prototype.wsClose = function(evt) {
	document.getElementById('connectDlg').className = "NetDbgDisconnected";
}

NetDbg.prototype.wsReceive = function(evt) {
	if (typeof(evt.data) == "string") {
		this.updateNames(evt.data);
	} else {
		var arr = new Uint32Array(evt.data);
		var snap = [];
		for (var i = 0; i < this.content.names.length; ++i) {
			snap.push({count: arr[i*3], size: arr[i*3+1], uncompressed: arr[i*3+2]});
			this.content.total[i*2] += arr[i*3+1];
			this.content.total[i*2 + 1] += arr[i*3];
		}
		this.content.snapshots.push(snap);
		this.invalidate();
		this.invalidateLegendStats();
	}
}

NetDbg.prototype.loadContent = function(content) {
	this.content = content;
	this.invalidate();
}

NetDbg.prototype.startDrag = function(evt) {
	this.grabX = evt.clientX;
	this.dragStarted = false;
	document.addEventListener("mousemove", this.dragEvt);
	document.addEventListener("mouseup", this.dragStopEvt);
}
NetDbg.prototype.stopDrag = function(evt) {
	document.removeEventListener("mousemove", this.dragEvt);
	document.removeEventListener("mouseup", this.dragStopEvt);
	if (!this.dragStarted)
		this.select(evt);
}
NetDbg.prototype.updateDrag = function(evt) {
	if (!this.dragStarted && Math.abs(this.grabX - evt.clientX) > 3) {
		this.dragStarted = true;
		this.offsetX = this.currentOffset();
		document.getElementById("liveUpdate").checked = false;
	}
	if (this.dragStarted) {
		this.offsetX -= evt.clientX - this.grabX;
		this.grabX = evt.clientX;
		if (this.offsetX < 0)
			this.offsetX = 0;
		if (this.offsetX > this.maxOffset())
			this.offsetX = this.maxOffset();
	}
	this.invalidate();
}

NetDbg.prototype.toggleLiveUpdate = function(enable) {
	if (enable) {
		this.offsetX = -1;
		this.invalidate();
	} else
		this.offsetX = this.currentOffset();
}

NetDbg.prototype.createName = function(name) {
	var div = document.createElement("div");
	div.style.display = "inline-block";
	div.style.width = "0";
	div.style.whiteSpace = "nowrap";
	div.appendChild(document.createTextNode(name));
	return div;
}
NetDbg.prototype.createCount = function(count, uncompressed) {
	var div = document.createElement("div");
	div.style.display = "inline-block";
	div.style.width = "0";
	div.style.whiteSpace = "nowrap";
	div.style.marginLeft = "200px";
	div.appendChild(document.createTextNode("" + count + " (" + uncompressed + ")"));
	return div;
}
NetDbg.prototype.createSize = function(sizeBits, sizeBytes) {
	var div = document.createElement("div");
	div.style.display = "inline-block";
	div.style.width = "0";
	div.style.whiteSpace = "nowrap";
	div.style.marginLeft = "200px";
	div.appendChild(document.createTextNode("" + sizeBits + " (" + sizeBytes + ")"));
	return div;
}
NetDbg.prototype.createInstSize = function(sizeBits, sizeBytes) {
	var div = document.createElement("div");
	div.style.display = "inline-block";
	div.style.width = "0";
	div.style.whiteSpace = "nowrap";
	div.style.marginLeft = "200px";
	div.appendChild(document.createTextNode("" + sizeBits + " (" + sizeBytes + ")"));
	return div;
}

NetDbg.prototype.select = function(evt) {
	var offset = evt.clientX;
	for (var p = this.canvas; p; p = p.offsetParent) {
		offset -= p.offsetLeft;
	}
	offset += this.currentOffset();
	this.selection = Math.floor(offset / this.SnapshotWidth);
	var descr = document.getElementById("descr");
	while (descr.firstChild)
		descr.removeChild(descr.firstChild);

	var content = this.content;
	if (this.selection >= 0 && this.selection < content.snapshots.length) {
		var div = document.createElement("div");
		descr.appendChild(div);
		var totalSize = 0;

		var headerDiv = document.createElement("div");
		var nameHead = this.createName("Ghost Type");
		headerDiv.appendChild(nameHead);

		var sizeHead = this.createSize("Total size bits", "bytes");
		headerDiv.appendChild(sizeHead);

		var countHead = this.createCount("Instances", "Uncompressed");
		headerDiv.appendChild(countHead);

		var isizeHead = this.createInstSize("Instance avg. size bits", "bytes");
		headerDiv.appendChild(isizeHead);

		descr.appendChild(headerDiv);
		for (var i = 0; i < content.snapshots[this.selection].length; ++i) {
			var type = content.snapshots[this.selection][i];
			if (type.count == 0)
				continue;

			var sectionDiv = document.createElement("div");

			var name = this.createName(content.names[i]);
			sectionDiv.appendChild(name);

			var size = this.createSize(type.size, Math.round(type.size / 8));
			sectionDiv.appendChild(size);

			var count = this.createCount(type.count, type.uncompressed);
			sectionDiv.appendChild(count);

			var isize = this.createInstSize(Math.round(type.size / type.count), Math.round(type.size / (8*type.count)));
			sectionDiv.appendChild(isize);

			descr.appendChild(sectionDiv);
			totalSize += type.size;
		}
		div.appendChild(document.createTextNode("Network frame " + this.selection + " - " + Math.round(totalSize / 8) +" bytes (" + totalSize + " bits)"));
	}
	this.invalidate();
}

NetDbg.prototype.invalidate = function() {
	if (this.pendingPresent == 0)
		this.pendingPresent = requestAnimationFrame(this.present.bind(this));
}

NetDbg.prototype.currentOffset = function() {
	if (this.offsetX < 0)
		return this.maxOffset();
	return this.offsetX;
}
NetDbg.prototype.maxOffset = function() {
	var maxOffset = this.content.snapshots.length * this.SnapshotWidth - this.canvas.width;
	if (maxOffset < 0)
		maxOffset = 0;
	return maxOffset;
}

NetDbg.prototype.present = function() {
	this.pendingPresent = 0;
	var content = this.content;

	var names = content.names;
	this.canvas.width = this.canvas.parentElement.offsetWidth;
	this.canvas.height = 480;

	var byteScale = 0.25 / 8;

	this.ctx.fillStyle = "black";
	this.ctx.fillRect(0,0,this.canvas.width, this.canvas.height);

	this.ctx.fillStyle = "gray";
	this.ctx.fillRect(0,this.canvas.height - 8000*byteScale,this.canvas.width, 1);

	var currentOffset = this.currentOffset();

	if (this.selection >= 0 && this.selection < content.snapshots.length) {
		this.ctx.fillStyle = "#fc0fc0";
		this.ctx.fillRect(this.selection*this.SnapshotWidth-this.SnapshotMargin/2 - currentOffset, 0, this.SnapshotWidth, this.canvas.height);
	}


	for (var i = 0; i < content.snapshots.length; ++i) {
		var total = 0;
		var totalCount = 0;
		var totalUncompressed = 0;
		for (var t = 0; t < content.snapshots[i].length; ++t) {
			this.ctx.fillStyle = this.Colors[t%this.Colors.length];
			this.ctx.fillRect(i*this.SnapshotWidth - currentOffset, this.canvas.height - byteScale * (total + content.snapshots[i][t].size), this.SnapshotWidth-this.SnapshotMargin, byteScale * content.snapshots[i][t].size);
			total += content.snapshots[i][t].size;
			totalCount += content.snapshots[i][t].count;
			totalUncompressed += content.snapshots[i][t].uncompressed;
		}
		if (totalCount > 0) {
			var uncompressedAlpha = totalUncompressed / totalCount;
			// Highlight frames where > 10% of the items were uncompressed
			if (uncompressedAlpha > 0.1) {
				uncompressedAlpha = uncompressedAlpha * 0.5 + 0.5;
				this.ctx.strokeStyle = "rgba(255,0,0," + uncompressedAlpha + ")";
				this.ctx.strokeRect(i*this.SnapshotWidth - currentOffset, this.canvas.height-byteScale*total, this.SnapshotWidth-this.SnapshotMargin, byteScale*total);
			}
		}
	}
}
