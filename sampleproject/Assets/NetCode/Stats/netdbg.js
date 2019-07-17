window.addEventListener("load", initNetDbg);

function initNetDbg() {
	g_debugger = new NetDbg();
}

function NetDbg() {
	this.selection = -1;
	this.offsetX = -1;
	this.dragEvt = this.updateDrag.bind(this);
	this.dragStopEvt = this.stopDrag.bind(this);

	document.getElementById("liveUpdate").checked = true;

	/*var loader = new XMLHttpRequest();
	loader.dbg = this;
	loader.addEventListener("load", function(){this.dbg.loadContent(this.response);});
	loader.open("GET", "snapshots.json");
	loader.responseType = "json";
	loader.send();*/

	this.content = [];
	//this.connect("localhost:8787");

	this.pendingPresent = 0;
	this.pendingStats = 0;
	this.invalidate();
}

NetDbg.prototype.SnapshotWidth = 10;
NetDbg.prototype.SnapshotMargin = 2;
NetDbg.prototype.Colors = ["red", "green", "blue", "yellow"];


NetDbg.prototype.updateNames = function(nameList) {
	var container = document.getElementById("connectionContainer");
	while (container.firstChild)
		container.removeChild(container.firstChild);
	var connections = JSON.parse(nameList);
	this.content = new Array(connections.length);
	for (var con = 0; con < this.content.length; ++con) {
		this.content[con] = {};
		this.content[con].container = document.createElement("div");
		if (con != 0)
			this.content[con].container.style.display = "none";
		var title = document.createElement("div");
		title.className = "ConnectionTitle";
		title.appendChild(document.createTextNode(connections[con].name));
		title.addEventListener("click", function(){
			if (this.nextElementSibling.style.display == "none") {
				this.nextElementSibling.style.display = "block";
				g_debugger.invalidate();
			} else
				this.nextElementSibling.style.display = "none";
			});
		container.appendChild(title);
		this.content[con].legend = document.createElement("div");
		this.content[con].legend.className = "LegendOverlay";
		this.content[con].container.appendChild(this.content[con].legend);
		this.content[con].canvas = document.createElement("canvas");
		this.content[con].ctx = this.content[con].canvas.getContext("2d");
		this.content[con].canvas.addEventListener("mousedown", this.startDrag.bind(this));
		this.content[con].container.appendChild(this.content[con].canvas);
		this.content[con].details = document.createElement("div");
		this.content[con].container.appendChild(this.content[con].details);
		container.appendChild(this.content[con].container);
		this.content[con].frames = [];
		this.content[con].names = connections[con].ghosts;
		this.content[con].total = new Array(this.content[con].names.length*2);
		var legend = this.content[con].legend;
		for (var i = 0; i < this.content[con].names.length; ++i) {
			this.content[con].total[i*2] = 0;
			this.content[con].total[i*2 + 1] = 0;
			var line = document.createElement("div");
			line.style.color = "white";
			line.style.padding = "2px";
			line.style.margin = "2px";
			line.style.borderWidth = "1px";
			line.style.borderColor = this.Colors[i%this.Colors.length];
			line.style.borderStyle = "solid";
			line.appendChild(document.createTextNode(this.content[con].names[i]));
			legend.appendChild(line);
		}
	}
}

NetDbg.prototype.invalidateLegendStats = function() {
	if (this.pendingStats)
		return;
	this.pendingStats = setTimeout(this.updateLegendStats.bind(this), 100);
}

NetDbg.prototype.updateLegendStats = function() {
	this.pendingStats = 0;
	for (var con = 0; con < this.content.length; ++con) {
		var legend = this.content[con].legend;
		var items = legend.children;
		for (var i = 0; i < this.content[con].names.length; ++i) {
			if (this.content[con].total[i*2] > 0) {
				var avgFrame = Math.round(this.content[con].total[i*2] / this.content[con].frames.length);
				var avgEnt = Math.round(this.content[con].total[i*2] / this.content[con].total[i*2 + 1]);
				items[i].firstChild.nodeValue = this.content[con].names[i] + ": " + avgFrame + " bits/frame, " + avgEnt + " bits/entity";
			}
		}
	}
}

NetDbg.prototype.connect = function(host) {
	document.getElementById('connectDlg').className = "NetDbgConnecting";
	// Clear the existing data
	this.content = [];
	var container = document.getElementById("connectionContainer");
	while (container.firstChild)
		container.removeChild(container.firstChild);
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
		var header = new Uint8Array(evt.data);
		var con = header[0];
		var time = new Uint32Array(evt.data, 4, 2);
		var arr = new Uint32Array(evt.data, 4*3);
		var snap = [];
		for (var i = 0; i < this.content[con].names.length; ++i) {
			snap.push({count: arr[i*3], size: arr[i*3+1], uncompressed: arr[i*3+2]});
			this.content[con].total[i*2] += arr[i*3+1];
			this.content[con].total[i*2 + 1] += arr[i*3];
		}
		this.content[con].frames.push({interpolationTick: time[0], predictionTick: time[1], snapshot: snap});
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
	for (var p = evt.target; p; p = p.offsetParent) {
		offset -= p.offsetLeft;
	}
	offset += this.currentOffset();
	this.selection = Math.floor(offset / this.SnapshotWidth);

	for (var con = 0; con < this.content.length; ++con) {
		var content = this.content[con];
		var descr = content.details;
		while (descr.firstChild)
			descr.removeChild(descr.firstChild);
		if (this.selection >= 0 && this.selection < content.frames.length) {
			var div = document.createElement("div");
			descr.appendChild(div);
			var tick = document.createElement("div");
			descr.appendChild(tick);
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
			for (var i = 0; i < content.frames[this.selection].snapshot.length; ++i) {
				var type = content.frames[this.selection].snapshot[i];
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
			tick.appendChild(document.createTextNode("Interpolation target " + content.frames[this.selection].interpolationTick + " (" +
				(this.selection==0?0:(content.frames[this.selection].interpolationTick-content.frames[this.selection-1].interpolationTick)) +
				") Prediction target " + content.frames[this.selection].predictionTick + " (" +
				(this.selection==0?0:(content.frames[this.selection].predictionTick-content.frames[this.selection-1].predictionTick)) + ")"));
		}
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
	var maxOffset = 0;
	for (var con = 0; con < this.content.length; ++con) {
		if (this.content[con].container.style.display == "none")
			continue;
		var ofs = this.content[con].frames.length * this.SnapshotWidth - this.content[con].container.offsetWidth;
		if (ofs > maxOffset)
			maxOffset = ofs;
	}
	return maxOffset;
}

NetDbg.prototype.present = function() {
	this.pendingPresent = 0;
	for (var con = 0; con < this.content.length; ++con) {
		var content = this.content[con];
		if (content.container.style.display == "none")
			continue;

		var names = content.names;
		content.canvas.width = content.canvas.parentElement.offsetWidth;
		content.canvas.height = 480;
		var dtHeight = 0;//80;
		var snapshotHeight = content.canvas.height - dtHeight;

		var byteScale = 0.25 / 8;

		content.ctx.fillStyle = "black";
		content.ctx.fillRect(0,0,content.canvas.width, content.canvas.height);

		content.ctx.fillStyle = "gray";
		content.ctx.fillRect(0,snapshotHeight - 8000*byteScale,content.canvas.width, 1);
		if (dtHeight > 0)
			content.ctx.fillRect(0,snapshotHeight + dtHeight-30,content.canvas.width, 1);
		content.ctx.fillStyle = "white";
		content.ctx.fillRect(0,snapshotHeight,content.canvas.width, 1);

		var currentOffset = this.currentOffset();

		if (this.selection >= 0 && this.selection < content.frames.length) {
			content.ctx.fillStyle = "#fc0fc0";
			content.ctx.fillRect(this.selection*this.SnapshotWidth-this.SnapshotMargin/2 - currentOffset, 0, this.SnapshotWidth, content.canvas.height);
		}


		for (var i = 0; i < content.frames.length; ++i) {
			var total = 0;
			var totalCount = 0;
			var totalUncompressed = 0;
			for (var t = 0; t < content.frames[i].snapshot.length; ++t) {
				content.ctx.fillStyle = this.Colors[t%this.Colors.length];
				content.ctx.fillRect(i*this.SnapshotWidth - currentOffset, snapshotHeight - byteScale * (total + content.frames[i].snapshot[t].size), this.SnapshotWidth-this.SnapshotMargin, byteScale * content.frames[i].snapshot[t].size);
				total += content.frames[i].snapshot[t].size;
				totalCount += content.frames[i].snapshot[t].count;
				totalUncompressed += content.frames[i].snapshot[t].uncompressed;
			}
			if (totalCount > 0) {
				var uncompressedAlpha = totalUncompressed / totalCount;
				// Highlight frames where > 10% of the items were uncompressed
				if (uncompressedAlpha > 0.1) {
					uncompressedAlpha = uncompressedAlpha * 0.5 + 0.5;
					content.ctx.strokeStyle = "rgba(255,0,0," + uncompressedAlpha + ")";
					content.ctx.strokeRect(i*this.SnapshotWidth - currentOffset, snapshotHeight-byteScale*total, this.SnapshotWidth-this.SnapshotMargin, byteScale*total);
				}
			}
			if (i > 0 && dtHeight > 0) {
				content.ctx.fillStyle = "red";
				content.ctx.fillRect(i*this.SnapshotWidth-this.SnapshotMargin/2 - currentOffset, snapshotHeight+dtHeight-20 - (content.frames[i].interpolationTick-content.frames[i-1].interpolationTick)*10, this.SnapshotWidth+this.SnapshotMargin/2, 2);
				content.ctx.fillStyle = "white";
				content.ctx.fillRect(i*this.SnapshotWidth-this.SnapshotMargin/2 - currentOffset, snapshotHeight+dtHeight-20 - (content.frames[i].predictionTick-content.frames[i-1].predictionTick)*10-2, this.SnapshotWidth+this.SnapshotMargin/2, 2);
			}
		}
	}
}
