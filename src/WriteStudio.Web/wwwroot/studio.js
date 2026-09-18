// WriteStudio Interactive Web Studio Client Engine
// Complete with Vector Drawing, Live Webcam PiP Compositing, MP4 Local Recording, and IndexedDB Video Library

// ==========================================
// IndexedDB Local Storage Manager
// ==========================================
class WriteStudioStorage {
    constructor() {
        this.dbName = 'WriteStudioDB';
        this.dbVersion = 1;
        this.db = null;
    }

    async init() {
        return new Promise((resolve, reject) => {
            const request = indexedDB.open(this.dbName, this.dbVersion);

            request.onupgradeneeded = (e) => {
                const db = e.target.result;
                if (!db.objectStoreNames.contains('recordings')) {
                    const store = db.createObjectStore('recordings', { keyPath: 'id' });
                    store.createIndex('createdAt', 'createdAt', { unique: false });
                }
                if (!db.objectStoreNames.contains('drafts')) {
                    db.createObjectStore('drafts', { keyPath: 'id' });
                }
            };

            request.onsuccess = (e) => {
                this.db = e.target.result;
                resolve(this.db);
            };

            request.onerror = (e) => reject(e.target.error);
        });
    }

    async saveRecording(recording) {
        if (!this.db) await this.init();
        return new Promise((resolve, reject) => {
            const tx = this.db.transaction('recordings', 'readwrite');
            const store = tx.objectStore('recordings');
            const req = store.put(recording);
            req.onsuccess = () => resolve(recording);
            req.onerror = (e) => reject(e.target.error);
        });
    }

    async getAllRecordings() {
        if (!this.db) await this.init();
        return new Promise((resolve, reject) => {
            const tx = this.db.transaction('recordings', 'readonly');
            const store = tx.objectStore('recordings');
            const req = store.getAll();
            req.onsuccess = () => {
                const results = req.result || [];
                results.sort((a, b) => new Date(b.createdAt) - new Date(a.createdAt));
                resolve(results);
            };
            req.onerror = (e) => reject(e.target.error);
        });
    }

    async getRecording(id) {
        if (!this.db) await this.init();
        return new Promise((resolve, reject) => {
            const tx = this.db.transaction('recordings', 'readonly');
            const store = tx.objectStore('recordings');
            const req = store.get(id);
            req.onsuccess = () => resolve(req.result);
            req.onerror = (e) => reject(e.target.error);
        });
    }

    async deleteRecording(id) {
        if (!this.db) await this.init();
        return new Promise((resolve, reject) => {
            const tx = this.db.transaction('recordings', 'readwrite');
            const store = tx.objectStore('recordings');
            const req = store.delete(id);
            req.onsuccess = () => resolve();
            req.onerror = (e) => reject(e.target.error);
        });
    }

    async clearAllRecordings() {
        if (!this.db) await this.init();
        return new Promise((resolve, reject) => {
            const tx = this.db.transaction('recordings', 'readwrite');
            const store = tx.objectStore('recordings');
            const req = store.clear();
            req.onsuccess = () => resolve();
            req.onerror = (e) => reject(e.target.error);
        });
    }
}

// ==========================================
// Main WriteStudio Application Engine
// ==========================================
class WriteStudioEngine {
    constructor() {
        this.storage = new WriteStudioStorage();
        this.canvas = document.getElementById('whiteboardCanvas');
        this.ctx = this.canvas.getContext('2d');
        this.canvasWrapper = document.getElementById('canvasWrapper');

        // Studio State
        this.activeTool = 'pen';
        this.activeColor = '#FFFFFF';
        this.activeThickness = 4;
        this.activeBackground = 'Blackboard';
        this.currentPageIndex = 0;
        this.pages = [
            { index: 0, title: 'Page 1', background: 'Blackboard', question: null, strokes: [] }
        ];
        this.undoStack = [];
        this.redoStack = [];

        // In-flight pointer state
        this.isPointerDown = false;
        this.currentStroke = null;
        this.shapeStartPoint = null;

        // Recording & Timeline State
        this.recordingState = 'Stopped';
        this.sessionStartTime = null;
        this.pauseStartTime = null;
        this.totalPausedDuration = 0;
        this.timelineEvents = [];
        this.timerInterval = null;
        this.animFrameId = null;

        // Presenter Reference Slides
        this.slides = [];
        this.currentSlideIndex = -1;
        this.slideTheme = 'light';
        this.slideFontSize = 16;

        // Hardware & Media Streams
        this.audioContext = null;
        this.analyserNode = null;
        this.audioStream = null;
        this.cameraStream = null;
        
        // Media Recorders
        this.audioRecorder = null;
        this.audioChunks = [];
        this.recordedAudioBlob = null;

        this.cameraRecorder = null;
        this.cameraChunks = [];
        this.recordedCameraBlob = null;

        // Combined in-browser local canvas + camera recorder
        this.canvasRecorder = null;
        this.canvasChunks = [];
        this.localVideoBlob = null;

        this.cameraLayout = {
            preset: 'BottomRight',
            isMirrored: true,
            isVisible: true
        };

        this.init();
    }

    async init() {
        await this.storage.init();
        this.setupCanvasSize();
        window.addEventListener('resize', () => this.setupCanvasSize());

        this.bindCanvasEvents();
        this.bindToolEvents();
        this.bindQuestionEvents();
        this.bindQuestionImportEvents();
        this.bindSlideEvents();
        this.bindSlideModalEvents();
        this.bindMediaEvents();
        this.bindRecordingEvents();
        this.bindExportEvents();
        this.bindLibraryEvents();
        this.bindMobileDrawerEvents();
        this.bindResizerEvents();

        this.renderCanvas();
        this.updateLibraryBadge();
        this.startContinuousRenderLoop();
    }

    setupCanvasSize() {
        const rect = this.canvasWrapper.getBoundingClientRect();
        const dpr = window.devicePixelRatio || 1;
        this.canvas.width = rect.width * dpr;
        this.canvas.height = rect.height * dpr;
        this.ctx.scale(dpr, dpr);
        this.cssWidth = rect.width;
        this.cssHeight = rect.height;
        this.renderCanvas();
        this.syncQuestionOverlay();
    }

    get currentPage() {
        return this.pages[this.currentPageIndex] || this.pages[0];
    }

    startContinuousRenderLoop() {
        const renderLoop = () => {
            if (this.recordingState === 'Recording' || (this.cameraStream && this.cameraLayout.isVisible && this.cameraLayout.preset !== 'Hidden')) {
                this.renderCanvas();
            }
            requestAnimationFrame(renderLoop);
        };
        requestAnimationFrame(renderLoop);
    }

    // ==========================================
    // Canvas Drawing & Pointer Events
    // ==========================================
    bindCanvasEvents() {
        this.canvas.addEventListener('pointerdown', (e) => this.onPointerDown(e));
        this.canvas.addEventListener('pointermove', (e) => this.onPointerMove(e));
        this.canvas.addEventListener('pointerup', (e) => this.onPointerUp(e));
        this.canvas.addEventListener('pointercancel', (e) => this.onPointerUp(e));
    }

    getCanvasPos(e) {
        const rect = this.canvas.getBoundingClientRect();
        return {
            x: (e.clientX - rect.left) * (1920 / this.cssWidth),
            y: (e.clientY - rect.top) * (1080 / this.cssHeight),
            pressure: e.pressure && e.pressure > 0 ? e.pressure : 0.5
        };
    }

    onPointerDown(e) {
        this.canvas.setPointerCapture(e.pointerId);
        this.isPointerDown = true;
        const pos = this.getCanvasPos(e);
        const timestamp = this.getElapsedSessionTime();

        if (this.activeTool === 'eraser') {
            this.eraseAt(pos.x, pos.y, this.activeThickness * 4);
            return;
        }

        if (this.isShapeTool(this.activeTool)) {
            this.shapeStartPoint = pos;
            return;
        }

        if (this.activeTool === 'text') {
            const text = prompt('Enter annotation text:');
            if (text) {
                const textStroke = {
                    id: this.generateGuid(),
                    pageIndex: this.currentPageIndex,
                    toolType: 'Text',
                    color: this.hexToRgba(this.activeColor),
                    thickness: this.activeThickness,
                    opacity: 1.0,
                    textContent: text,
                    fontSize: Math.max(18, this.activeThickness * 6),
                    points: [{ x: pos.x, y: pos.y, pressure: 1.0, timestamp: timestamp }],
                    startTime: timestamp,
                    endTime: timestamp
                };
                this.addStroke(textStroke);
            }
            this.isPointerDown = false;
            return;
        }

        const strokeColor = this.activeTool === 'highlighter' ? this.hexToRgba('#FFF176', 0.45) : this.hexToRgba(this.activeColor);
        const strokeThickness = this.activeTool === 'highlighter' ? Math.max(this.activeThickness * 3, 20) : this.activeThickness;

        this.currentStroke = {
            id: this.generateGuid(),
            pageIndex: this.currentPageIndex,
            toolType: this.activeTool === 'highlighter' ? 'Highlighter' : (this.activeTool === 'pencil' ? 'Pencil' : 'Pen'),
            color: strokeColor,
            thickness: strokeThickness,
            opacity: this.activeTool === 'highlighter' ? 0.45 : 1.0,
            points: [{ x: pos.x, y: pos.y, pressure: pos.pressure, timestamp: timestamp }],
            startTime: timestamp,
            endTime: timestamp
        };

        if (this.recordingState === 'Recording') {
            this.recordTimelineEvent({
                $eventType: 'StrokeStarted',
                timestamp: timestamp,
                stroke: JSON.parse(JSON.stringify(this.currentStroke))
            });
        }

        this.renderCanvas();
    }

    onPointerMove(e) {
        if (!this.isPointerDown) return;
        const pos = this.getCanvasPos(e);
        const timestamp = this.getElapsedSessionTime();

        if (this.activeTool === 'eraser') {
            this.eraseAt(pos.x, pos.y, this.activeThickness * 4);
            return;
        }

        if (this.shapeStartPoint) {
            this.renderCanvas();
            this.drawShapePreview(this.shapeStartPoint, pos);
            return;
        }

        if (this.currentStroke) {
            const point = { x: pos.x, y: pos.y, pressure: pos.pressure, timestamp: timestamp };
            this.currentStroke.points.push(point);
            this.currentStroke.endTime = timestamp;

            if (this.recordingState === 'Recording') {
                this.recordTimelineEvent({
                    $eventType: 'StrokePointAdded',
                    timestamp: timestamp,
                    strokeId: this.currentStroke.id,
                    point: point
                });
            }

            this.renderCanvas();
        }
    }

    onPointerUp(e) {
        if (!this.isPointerDown) return;
        this.isPointerDown = false;
        const pos = this.getCanvasPos(e);
        const timestamp = this.getElapsedSessionTime();

        if (this.shapeStartPoint) {
            const points = this.generateShapePoints(this.activeTool, this.shapeStartPoint, pos, timestamp);
            const shapeStroke = {
                id: this.generateGuid(),
                pageIndex: this.currentPageIndex,
                toolType: this.capitalize(this.activeTool),
                color: this.hexToRgba(this.activeColor),
                thickness: this.activeThickness,
                opacity: 1.0,
                points: points,
                startTime: timestamp,
                endTime: timestamp
            };
            this.addStroke(shapeStroke);
            this.shapeStartPoint = null;
            return;
        }

        if (this.currentStroke) {
            this.addStroke(this.currentStroke);
            if (this.recordingState === 'Recording') {
                this.recordTimelineEvent({
                    $eventType: 'StrokeCompleted',
                    timestamp: timestamp,
                    strokeId: this.currentStroke.id
                });
            }
            this.currentStroke = null;
        }
    }

    isShapeTool(tool) {
        return ['line', 'rectangle', 'circle', 'arrow'].includes(tool);
    }

    generateShapePoints(tool, p1, p2, timestamp) {
        if (tool === 'line') {
            return [
                { x: p1.x, y: p1.y, pressure: 0.5, timestamp: timestamp },
                { x: p2.x, y: p2.y, pressure: 0.5, timestamp: timestamp }
            ];
        }
        if (tool === 'rectangle') {
            const left = Math.min(p1.x, p2.x), right = Math.max(p1.x, p2.x);
            const top = Math.min(p1.y, p2.y), bottom = Math.max(p1.y, p2.y);
            return [
                { x: left, y: top, pressure: 0.5, timestamp: timestamp },
                { x: right, y: top, pressure: 0.5, timestamp: timestamp },
                { x: right, y: bottom, pressure: 0.5, timestamp: timestamp },
                { x: left, y: bottom, pressure: 0.5, timestamp: timestamp },
                { x: left, y: top, pressure: 0.5, timestamp: timestamp }
            ];
        }
        if (tool === 'circle') {
            const cx = (p1.x + p2.x) / 2, cy = (p1.y + p2.y) / 2;
            const rx = Math.abs(p2.x - p1.x) / 2, ry = Math.abs(p2.y - p1.y) / 2;
            const points = [];
            const segments = 32;
            for (let i = 0; i <= segments; i++) {
                const angle = (2 * Math.PI * i) / segments;
                points.push({
                    x: cx + rx * Math.cos(angle),
                    y: cy + ry * Math.sin(angle),
                    pressure: 0.5,
                    timestamp: timestamp
                });
            }
            return points;
        }
        if (tool === 'arrow') {
            const angle = Math.atan2(p2.y - p1.y, p2.x - p1.x);
            const headLen = 24;
            const a1 = angle - Math.PI / 6;
            const a2 = angle + Math.PI / 6;
            return [
                { x: p1.x, y: p1.y, pressure: 0.5, timestamp: timestamp },
                { x: p2.x, y: p2.y, pressure: 0.5, timestamp: timestamp },
                { x: p2.x - headLen * Math.cos(a1), y: p2.y - headLen * Math.sin(a1), pressure: 0.5, timestamp: timestamp },
                { x: p2.x, y: p2.y, pressure: 0.5, timestamp: timestamp },
                { x: p2.x - headLen * Math.cos(a2), y: p2.y - headLen * Math.sin(a2), pressure: 0.5, timestamp: timestamp }
            ];
        }
        return [];
    }

    addStroke(stroke) {
        this.currentPage.strokes.push(stroke);
        this.undoStack.push({ type: 'addStroke', stroke: stroke, pageIndex: this.currentPageIndex });
        this.redoStack = [];
        this.renderCanvas();
    }

    eraseAt(x, y, radius) {
        const page = this.currentPage;
        const remaining = [];
        const erased = [];

        for (const stroke of page.strokes) {
            let hit = false;
            for (const pt of stroke.points) {
                const dist = Math.hypot(pt.x - x, pt.y - y);
                if (dist <= radius + stroke.thickness) {
                    hit = true;
                    break;
                }
            }
            if (hit) {
                erased.push(stroke);
            } else {
                remaining.push(stroke);
            }
        }

        if (erased.length > 0) {
            page.strokes = remaining;
            this.undoStack.push({ type: 'eraseStrokes', strokes: erased, pageIndex: this.currentPageIndex });
            this.redoStack = [];

            if (this.recordingState === 'Recording') {
                this.recordTimelineEvent({
                    $eventType: 'StrokesErased',
                    timestamp: this.getElapsedSessionTime(),
                    pageIndex: this.currentPageIndex,
                    erasedStrokeIds: erased.map(s => s.id)
                });
            }

            this.renderCanvas();
        }
    }

    // ==========================================
    // Canvas Rendering Loop & Webcam Compositing
    // ==========================================
    renderCanvas() {
        if (!this.ctx) return;

        const scaleX = this.cssWidth / 1920;
        const scaleY = this.cssHeight / 1080;

        this.ctx.save();
        this.ctx.clearRect(0, 0, this.cssWidth, this.cssHeight);

        // 1. Background
        this.drawBackground(this.currentPage.background);

        this.ctx.scale(scaleX, scaleY);

        // 2. Render MCQ / Question Card (if page contains a question)
        if (this.currentPage.question && this.currentPage.question.questionText) {
            this.drawQuestionCard(this.currentPage.question, this.currentPage.background);
        }

        // 3. Render Page Strokes (strokes and drawings appear on top/around the question card)
        for (const stroke of this.currentPage.strokes) {
            this.drawStroke(stroke);
        }

        if (this.currentStroke) {
            this.drawStroke(this.currentStroke);
        }

        this.ctx.restore();

        // 4. Render Live Moving Presenter Webcam PiP Layer directly onto canvas
        const webcamVideo = document.getElementById('webcamVideo');
        if (this.cameraStream && webcamVideo && webcamVideo.readyState >= 2 && this.cameraLayout.isVisible && this.cameraLayout.preset !== 'Hidden') {
            this.drawWebcamPip(webcamVideo);
        }

        // 5. Synchronize On-Canvas Action Overlay position
        this.syncQuestionOverlay();
    }

    drawQuestionCard(question, background) {
        if (!question || !question.questionText) return;

        const isDarkBg = background === 'Blackboard' || background === 'DarkGrid' || background === 'DarkRuled';
        const cardX = question.x || 80;
        const cardY = question.y || 80;
        const cardW = Math.max(500, question.width || 1000);
        const padding = 24;
        const innerW = cardW - (padding * 2);
        let currentY = cardY + padding;

        const fontSize = Math.max(16, question.fontSize || 26);
        const titleFontSize = Math.round(fontSize * 0.75);
        const optionFontSize = Math.round(fontSize * 0.85);
        const fontFamily = question.fontFamily || '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif';

        // Helper to wrap text cleanly
        const wrapText = (text, maxW, font) => {
            this.ctx.font = font;
            const lines = [];
            const paragraphs = String(text).split('\n');
            for (const para of paragraphs) {
                const words = para.split(' ');
                let curLine = '';
                for (const word of words) {
                    const testLine = curLine ? curLine + ' ' + word : word;
                    const w = this.ctx.measureText(testLine).width;
                    if (w > maxW && curLine) {
                        lines.push(curLine);
                        curLine = word;
                    } else {
                        curLine = testLine;
                    }
                }
                if (curLine) lines.push(curLine);
            }
            return lines;
        };

        const qFont = `600 ${fontSize}px ${fontFamily}`;
        const qLines = wrapText(question.questionText, innerW, qFont);
        const qLineHeight = fontSize * 1.35;

        // Calculate card height dynamically
        let estimatedH = padding;
        if (question.questionNumber) estimatedH += titleFontSize + 12;
        estimatedH += (qLines.length * qLineHeight) + 16;

        const optHeight = Math.max(44, optionFontSize * 2.2);
        const optSpacing = 10;
        const options = question.options || [];
        estimatedH += (options.length * (optHeight + optSpacing));

        if (question.isAnswerRevealed && question.correctAnswer) {
            estimatedH += 36;
        }
        estimatedH += padding;
        question._cardHeight = estimatedH;

        // 1. Draw Card Background Container
        this.ctx.save();
        this.ctx.fillStyle = isDarkBg ? 'rgba(30, 41, 59, 0.95)' : 'rgba(255, 255, 255, 0.96)';
        this.ctx.strokeStyle = isDarkBg ? 'rgba(56, 189, 248, 0.7)' : 'rgba(203, 213, 225, 0.85)';
        this.ctx.lineWidth = 2;
        this.ctx.shadowColor = 'rgba(0, 0, 0, 0.25)';
        this.ctx.shadowBlur = 12;
        this.ctx.shadowOffsetY = 4;

        this.ctx.beginPath();
        if (this.ctx.roundRect) {
            this.ctx.roundRect(cardX, cardY, cardW, estimatedH, 14);
        } else {
            this.ctx.rect(cardX, cardY, cardW, estimatedH);
        }
        this.ctx.fill();
        this.ctx.stroke();
        this.ctx.restore();

        // 2. Draw Question Number / Title Header
        this.ctx.save();
        this.ctx.textBaseline = 'top';
        if (question.questionNumber) {
            this.ctx.font = `bold ${titleFontSize}px ${fontFamily}`;
            this.ctx.fillStyle = isDarkBg ? '#38BDF8' : '#0284C7';
            this.ctx.fillText(question.questionNumber.toUpperCase(), cardX + padding, currentY);
            currentY += titleFontSize + 12;
        }

        // 3. Draw Question Text Prompt Lines
        this.ctx.font = qFont;
        this.ctx.fillStyle = isDarkBg ? '#FFFFFF' : '#0F172A';
        for (const line of qLines) {
            this.ctx.fillText(line, cardX + padding, currentY);
            currentY += qLineHeight;
        }
        currentY += 12;

        // 4. Draw Options
        const optFont = `400 ${optionFontSize}px ${fontFamily}`;
        const badgeFont = `bold ${Math.round(optionFontSize * 0.9)}px ${fontFamily}`;
        const checkFont = `bold ${Math.round(optionFontSize * 1.05)}px ${fontFamily}`;

        for (let i = 0; i < options.length; i++) {
            const opt = options[i];
            const optY = currentY;
            const isCorrect = question.isAnswerRevealed && (question.correctAnswer && question.correctAnswer.toUpperCase() === (opt.label || '').toUpperCase());

            // Option Pill Box
            this.ctx.beginPath();
            if (this.ctx.roundRect) {
                this.ctx.roundRect(cardX + padding, optY, innerW, optHeight, 8);
            } else {
                this.ctx.rect(cardX + padding, optY, innerW, optHeight);
            }

            if (isCorrect) {
                this.ctx.fillStyle = isDarkBg ? 'rgba(22, 101, 52, 0.9)' : 'rgba(220, 252, 231, 0.95)';
                this.ctx.strokeStyle = '#22C55E';
                this.ctx.lineWidth = 2;
            } else {
                this.ctx.fillStyle = isDarkBg ? 'rgba(15, 23, 42, 0.85)' : 'rgba(248, 250, 252, 0.9)';
                this.ctx.strokeStyle = isDarkBg ? 'rgba(51, 65, 85, 0.8)' : 'rgba(226, 232, 240, 0.9)';
                this.ctx.lineWidth = 1;
            }
            this.ctx.fill();
            this.ctx.stroke();

            // Letter Badge (e.g. A, B, C, D)
            const badgeSize = optHeight - 12;
            const badgeX = cardX + padding + 8;
            const badgeY = optY + 6;

            this.ctx.beginPath();
            if (this.ctx.roundRect) {
                this.ctx.roundRect(badgeX, badgeY, badgeSize, badgeSize, 6);
            } else {
                this.ctx.rect(badgeX, badgeY, badgeSize, badgeSize);
            }
            this.ctx.fillStyle = isCorrect ? '#22C55E' : (isDarkBg ? '#38BDF8' : '#0284C7');
            this.ctx.fill();

            // Badge letter text
            this.ctx.font = badgeFont;
            this.ctx.fillStyle = '#FFFFFF';
            this.ctx.textAlign = 'center';
            this.ctx.textBaseline = 'middle';
            this.ctx.fillText(opt.label || String.fromCharCode(65 + i), badgeX + (badgeSize / 2), badgeY + (badgeSize / 2));

            // Option text content
            this.ctx.font = optFont;
            this.ctx.fillStyle = isDarkBg ? '#F1F5F9' : '#1E293B';
            this.ctx.textAlign = 'left';
            this.ctx.textBaseline = 'middle';
            this.ctx.fillText(opt.text || '', badgeX + badgeSize + 12, optY + (optHeight / 2));

            // Checkmark if correct & revealed
            if (isCorrect) {
                this.ctx.font = checkFont;
                this.ctx.fillStyle = '#22C55E';
                this.ctx.textAlign = 'right';
                this.ctx.textBaseline = 'middle';
                this.ctx.fillText('✓ Correct', cardX + padding + innerW - 16, optY + (optHeight / 2));
            }

            currentY += optHeight + optSpacing;
        }

        // 5. Draw Revealed Answer Banner
        if (question.isAnswerRevealed && question.correctAnswer) {
            this.ctx.font = `bold ${Math.round(optionFontSize * 1.05)}px ${fontFamily}`;
            this.ctx.fillStyle = '#22C55E';
            this.ctx.textAlign = 'left';
            this.ctx.textBaseline = 'top';
            this.ctx.fillText(`✓ Correct Answer: Option ${question.correctAnswer}`, cardX + padding, currentY + 12);
        }

        this.ctx.restore();
    }

    syncQuestionOverlay() {
        const overlay = document.getElementById('canvasQuestionOverlay');
        if (!overlay) return;

        const q = this.currentPage?.question;
        if (!q || !q.questionText) {
            overlay.style.display = 'none';
            return;
        }

        const scaleX = this.cssWidth / 1920;
        const scaleY = this.cssHeight / 1080;

        const left = (q.x || 80) * scaleX;
        let top = ((q.y || 80) - 44) * scaleY;
        if (top < 8) {
            top = ((q.y || 80) + 8) * scaleY;
        }

        overlay.style.display = 'flex';
        overlay.style.left = `${Math.max(8, left)}px`;
        overlay.style.top = `${Math.max(8, top)}px`;

        const btnToggleReveal = document.getElementById('btnToggleAnswerReveal');
        if (btnToggleReveal) {
            if (q.isAnswerRevealed) {
                btnToggleReveal.textContent = '🙈 Hide Answer';
                btnToggleReveal.classList.remove('btn-success');
                btnToggleReveal.classList.add('btn-warning');
            } else {
                btnToggleReveal.textContent = '👁 Reveal Answer';
                btnToggleReveal.classList.remove('btn-warning');
                btnToggleReveal.classList.add('btn-success');
            }
        }
    }

    drawWebcamPip(video) {
        const w = 1920;
        const h = 1080;
        const preset = this.cameraLayout.preset || 'BottomRight';
        
        let pipW = 420;
        let pipH = 236; // 16:9 ratio
        let pipX = w - pipW - 36;
        let pipY = h - pipH - 36;

        if (preset === 'BottomLeft') {
            pipX = 36;
            pipY = h - pipH - 36;
        } else if (preset === 'TopRight') {
            pipX = w - pipW - 36;
            pipY = 36;
        } else if (preset === 'TopLeft') {
            pipX = 36;
            pipY = 36;
        } else if (preset === 'Fullscreen') {
            pipX = 0;
            pipY = 0;
            pipW = w;
            pipH = h;
        }

        const scaleX = this.cssWidth / 1920;
        const scaleY = this.cssHeight / 1080;
        const radius = preset === 'Fullscreen' ? 0 : 12;

        this.ctx.save();
        this.ctx.scale(scaleX, scaleY);

        // Clip rounded rectangle for webcam PiP
        this.ctx.beginPath();
        if (this.ctx.roundRect) {
            this.ctx.roundRect(pipX, pipY, pipW, pipH, radius);
        } else {
            this.ctx.rect(pipX, pipY, pipW, pipH);
        }
        this.ctx.clip();

        // Draw webcam video with optional mirror
        if (this.cameraLayout.isMirrored) {
            this.ctx.translate(pipX + pipW, pipY);
            this.ctx.scale(-1, 1);
            this.ctx.drawImage(video, 0, 0, pipW, pipH);
        } else {
            this.ctx.drawImage(video, pipX, pipY, pipW, pipH);
        }

        this.ctx.restore();

        // Draw camera PiP border
        if (preset !== 'Fullscreen') {
            this.ctx.save();
            this.ctx.scale(scaleX, scaleY);
            this.ctx.beginPath();
            if (this.ctx.roundRect) {
                this.ctx.roundRect(pipX, pipY, pipW, pipH, radius);
            } else {
                this.ctx.rect(pipX, pipY, pipW, pipH);
            }
            this.ctx.strokeStyle = '#38BDF8';
            this.ctx.lineWidth = 3;
            this.ctx.stroke();
            this.ctx.restore();
        }
    }

    drawBackground(bg) {
        const w = this.cssWidth, h = this.cssHeight;

        if (bg === 'Blackboard' || bg === 'DarkGrid' || bg === 'DarkRuled') {
            this.ctx.fillStyle = '#1C2127';
            this.ctx.fillRect(0, 0, w, h);
        } else {
            this.ctx.fillStyle = '#FFFFFF';
            this.ctx.fillRect(0, 0, w, h);
        }

        if (bg === 'DarkGrid' || bg === 'LightGrid') {
            this.ctx.strokeStyle = bg === 'DarkGrid' ? '#323A45' : '#E6EBF0';
            this.ctx.lineWidth = 1;
            for (let x = 0; x < w; x += 36) {
                this.ctx.beginPath();
                this.ctx.moveTo(x, 0);
                this.ctx.lineTo(x, h);
                this.ctx.stroke();
            }
            for (let y = 0; y < h; y += 36) {
                this.ctx.beginPath();
                this.ctx.moveTo(0, y);
                this.ctx.lineTo(w, y);
                this.ctx.stroke();
            }
        } else if (bg === 'Ruled' || bg === 'DarkRuled') {
            this.ctx.strokeStyle = bg === 'DarkRuled' ? '#323A45' : '#D2E1F5';
            this.ctx.lineWidth = 1.2;
            for (let y = 60; y < h; y += 32) {
                this.ctx.beginPath();
                this.ctx.moveTo(0, y);
                this.ctx.lineTo(w, y);
                this.ctx.stroke();
            }
        }
    }

    drawStroke(stroke) {
        if (!stroke.points || stroke.points.length === 0) return;

        const c = stroke.color;
        this.ctx.strokeStyle = `rgba(${c.R}, ${c.G}, ${c.B}, ${stroke.opacity})`;
        this.ctx.fillStyle = `rgba(${c.R}, ${c.G}, ${c.B}, ${stroke.opacity})`;
        this.ctx.lineCap = 'round';
        this.ctx.lineJoin = 'round';
        this.ctx.lineWidth = stroke.thickness;

        if (stroke.toolType === 'Text' && stroke.textContent) {
            this.ctx.font = `${stroke.fontSize || 24}px sans-serif`;
            this.ctx.fillText(stroke.textContent, stroke.points[0].x, stroke.points[0].y);
            return;
        }

        if (stroke.points.length === 1) {
            this.ctx.beginPath();
            this.ctx.arc(stroke.points[0].x, stroke.points[0].y, stroke.thickness / 2, 0, Math.PI * 2);
            this.ctx.fill();
            return;
        }

        this.ctx.beginPath();
        this.ctx.moveTo(stroke.points[0].x, stroke.points[0].y);

        for (let i = 1; i < stroke.points.length; i++) {
            const p = stroke.points[i];
            this.ctx.lineTo(p.x, p.y);
        }
        this.ctx.stroke();
    }

    drawShapePreview(p1, p2) {
        const points = this.generateShapePoints(this.activeTool, p1, p2, '00:00:00');
        if (points.length === 0) return;

        const scaleX = this.cssWidth / 1920;
        const scaleY = this.cssHeight / 1080;

        this.ctx.save();
        this.ctx.scale(scaleX, scaleY);
        this.ctx.strokeStyle = this.activeColor;
        this.ctx.lineWidth = this.activeThickness;
        this.ctx.lineCap = 'round';
        this.ctx.lineJoin = 'round';

        this.ctx.beginPath();
        this.ctx.moveTo(points[0].x, points[0].y);
        for (let i = 1; i < points.length; i++) {
            this.ctx.lineTo(points[i].x, points[i].y);
        }
        this.ctx.stroke();
        this.ctx.restore();
    }

    // ==========================================
    // Tool & Palette Controls
    // ==========================================
    bindToolEvents() {
        const allToolBtns = document.querySelectorAll('.tool-btn');
        allToolBtns.forEach(btn => {
            btn.addEventListener('click', () => {
                if (btn.dataset.tool === 'question') {
                    this.openQuestionModal();
                    return;
                }
                if (btn.dataset.tool === 'import-question') {
                    this.openQuestionImportModal();
                    return;
                }
                allToolBtns.forEach(b => {
                    if (b.dataset.tool === btn.dataset.tool) b.classList.add('active');
                    else b.classList.remove('active');
                });
                this.activeTool = btn.dataset.tool;
            });
        });

        const allColorBtns = document.querySelectorAll('.color-btn');
        allColorBtns.forEach(btn => {
            btn.addEventListener('click', () => {
                allColorBtns.forEach(b => {
                    if (b.dataset.color && btn.dataset.color && b.dataset.color.toLowerCase() === btn.dataset.color.toLowerCase()) {
                        b.classList.add('active');
                    } else {
                        b.classList.remove('active');
                    }
                });
                this.activeColor = btn.dataset.color;
            });
        });

        const slider = document.getElementById('thicknessSlider');
        const thicknessVal = document.getElementById('thicknessValue');
        const btnCycle = document.getElementById('btnMobileThicknessCycle');
        const thicknessSteps = [2, 4, 8, 14, 24];

        slider.addEventListener('input', (e) => {
            this.activeThickness = parseInt(e.target.value, 10);
            thicknessVal.textContent = this.activeThickness;
            if (btnCycle) btnCycle.textContent = `${this.activeThickness}px`;
        });

        if (btnCycle) {
            btnCycle.addEventListener('click', () => {
                let curIdx = thicknessSteps.indexOf(this.activeThickness);
                let nextIdx = (curIdx + 1) % thicknessSteps.length;
                this.activeThickness = thicknessSteps[nextIdx];
                btnCycle.textContent = `${this.activeThickness}px`;
                slider.value = this.activeThickness;
                thicknessVal.textContent = this.activeThickness;
            });
        }

        const btnMobileUndo = document.getElementById('btnMobileUndo');
        const btnMobileRedo = document.getElementById('btnMobileRedo');
        if (btnMobileUndo) btnMobileUndo.addEventListener('click', () => this.undo());
        if (btnMobileRedo) btnMobileRedo.addEventListener('click', () => this.redo());

        const bgSelector = document.getElementById('bgSelector');
        bgSelector.addEventListener('change', (e) => {
            this.activeBackground = e.target.value;
            this.currentPage.background = this.activeBackground;
            if (this.recordingState === 'Recording') {
                this.recordTimelineEvent({
                    $eventType: 'BackgroundChanged',
                    timestamp: this.getElapsedSessionTime(),
                    pageIndex: this.currentPageIndex,
                    newBackground: this.activeBackground
                });
            }
            this.renderCanvas();
        });

        // Pages
        document.getElementById('btnAddPage').addEventListener('click', () => {
            const newIndex = this.pages.length;
            this.pages.push({
                index: newIndex,
                title: `Page ${newIndex + 1}`,
                background: this.activeBackground,
                question: null,
                strokes: []
            });
            this.setPageIndex(newIndex);
        });

        document.getElementById('btnDeletePage').addEventListener('click', () => {
            if (this.pages.length > 1) {
                this.pages.splice(this.currentPageIndex, 1);
                this.pages.forEach((p, idx) => { p.index = idx; p.title = `Page ${idx + 1}`; });
                this.setPageIndex(Math.min(this.currentPageIndex, this.pages.length - 1));
            }
        });

        document.getElementById('btnPrevPage').addEventListener('click', () => {
            if (this.currentPageIndex > 0) this.setPageIndex(this.currentPageIndex - 1);
        });

        document.getElementById('btnNextPage').addEventListener('click', () => {
            if (this.currentPageIndex < this.pages.length - 1) this.setPageIndex(this.currentPageIndex + 1);
        });

        // Undo / Redo / Clear
        const clearCanvasHandler = () => {
            if (this.currentPage.strokes.length === 0) return;
            this.undoStack.push({ type: 'clearPage', strokes: [...this.currentPage.strokes], pageIndex: this.currentPageIndex });
            this.currentPage.strokes = [];
            this.renderCanvas();
        };

        const btnClearCanvas = document.getElementById('btnClearCanvas');
        const btnMobileClearCanvas = document.getElementById('btnMobileClearCanvas');

        if (btnClearCanvas) btnClearCanvas.addEventListener('click', clearCanvasHandler);
        if (btnMobileClearCanvas) btnMobileClearCanvas.addEventListener('click', clearCanvasHandler);
        if (btnUndo) btnUndo.addEventListener('click', () => this.undo());
        if (btnRedo) btnRedo.addEventListener('click', () => this.redo());

        window.addEventListener('keydown', (e) => {
            if ((e.metaKey || e.ctrlKey) && e.key === 'z') {
                e.preventDefault();
                if (e.shiftKey) this.redo();
                else this.undo();
            } else if ((e.metaKey || e.ctrlKey) && e.key === 'y') {
                e.preventDefault();
                this.redo();
            }
        });
    }

    setPageIndex(idx) {
        if (idx === this.currentPageIndex) return;
        const prevIdx = this.currentPageIndex;
        this.currentPageIndex = idx;
        document.getElementById('pageIndicator').textContent = `Page ${idx + 1} of ${this.pages.length}`;
        document.getElementById('bgSelector').value = this.currentPage.background;

        if (this.recordingState === 'Recording') {
            this.recordTimelineEvent({
                $eventType: 'PageChanged',
                timestamp: this.getElapsedSessionTime(),
                previousPageIndex: prevIdx,
                newPageIndex: idx
            });
        }

        this.renderCanvas();
        this.syncQuestionOverlay();
    }

    undo() {
        if (this.undoStack.length === 0) return;
        const action = this.undoStack.pop();
        if (action.type === 'addStroke') {
            const page = this.pages[action.pageIndex];
            page.strokes = page.strokes.filter(s => s.id !== action.stroke.id);
            this.redoStack.push(action);
        } else if (action.type === 'eraseStrokes') {
            const page = this.pages[action.pageIndex];
            page.strokes.push(...action.strokes);
            this.redoStack.push(action);
        } else if (action.type === 'clearPage') {
            const page = this.pages[action.pageIndex];
            page.strokes = action.strokes;
            this.redoStack.push(action);
        } else if (action.type === 'setQuestion') {
            const page = this.pages[action.pageIndex];
            page.question = action.prevQuestion ? { ...action.prevQuestion } : null;
            this.redoStack.push(action);
        } else if (action.type === 'deleteQuestion') {
            const page = this.pages[action.pageIndex];
            page.question = action.prevQuestion ? { ...action.prevQuestion } : null;
            this.redoStack.push(action);
        } else if (action.type === 'importQuestions') {
            this.pages = JSON.parse(JSON.stringify(action.prevPages));
            this.currentPageIndex = Math.min(action.prevPageIndex, this.pages.length - 1);
            this.redoStack.push(action);
        }
        this.renderCanvas();
        this.syncQuestionOverlay();
    }

    redo() {
        if (this.redoStack.length === 0) return;
        const action = this.redoStack.pop();
        if (action.type === 'addStroke') {
            const page = this.pages[action.pageIndex];
            page.strokes.push(action.stroke);
            this.undoStack.push(action);
        } else if (action.type === 'eraseStrokes') {
            const page = this.pages[action.pageIndex];
            const ids = new Set(action.strokes.map(s => s.id));
            page.strokes = page.strokes.filter(s => !ids.has(s.id));
            this.undoStack.push(action);
        } else if (action.type === 'clearPage') {
            const page = this.pages[action.pageIndex];
            page.strokes = [];
            this.undoStack.push(action);
        } else if (action.type === 'setQuestion') {
            const page = this.pages[action.pageIndex];
            page.question = action.nextQuestion ? { ...action.nextQuestion } : null;
            this.undoStack.push(action);
        } else if (action.type === 'deleteQuestion') {
            const page = this.pages[action.pageIndex];
            page.question = null;
            this.undoStack.push(action);
        } else if (action.type === 'importQuestions') {
            this.pages = JSON.parse(JSON.stringify(action.nextPages));
            this.currentPageIndex = Math.min(action.nextPageIndex, this.pages.length - 1);
            this.undoStack.push(action);
        }
        this.renderCanvas();
        this.syncQuestionOverlay();
    }

    // ==========================================
    // MCQ / Question Component Logic
    // ==========================================
    escapeHtml(str) {
        if (!str) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
    }

    openQuestionModal() {
        const modal = document.getElementById('questionEditorModal');
        if (!modal) return;

        const q = this.currentPage?.question;
        const inputNum = document.getElementById('inputQuestionNumber');
        const inputText = document.getElementById('inputQuestionText');
        const selectFontSize = document.getElementById('selectQuestionFontSize');
        const checkReveal = document.getElementById('checkRevealAnswerOnSave');
        const btnDeleteInModal = document.getElementById('btnDeleteQuestionInModal');
        const optionsContainer = document.getElementById('questionOptionsContainer');
        const selectCorrectAnswer = document.getElementById('selectCorrectAnswer');
        const optionLabels = ['A', 'B', 'C', 'D', 'E', 'F'];

        const updateCorrectAnswerSelect = (labels, selectedVal) => {
            selectCorrectAnswer.innerHTML = '<option value="">None (Ungraded / Poll)</option>';
            labels.forEach(lbl => {
                const opt = document.createElement('option');
                opt.value = lbl;
                opt.textContent = `Option ${lbl}`;
                if (lbl === selectedVal) opt.selected = true;
                selectCorrectAnswer.appendChild(opt);
            });
        };

        const renderOptionRows = (optionsData = []) => {
            optionsContainer.innerHTML = '';
            const count = Math.max(2, Math.min(6, optionsData.length || 4));

            for (let i = 0; i < count; i++) {
                const lbl = optionLabels[i];
                const textVal = optionsData[i] ? optionsData[i].text : '';
                const row = document.createElement('div');
                row.className = 'question-option-row';
                row.dataset.label = lbl;
                row.innerHTML = `
                    <span class="option-badge-label">${lbl}</span>
                    <input type="text" class="form-control question-option-input" placeholder="Option ${lbl} text..." value="${this.escapeHtml(textVal)}">
                    <button type="button" class="btn btn-xs btn-outline-danger btn-remove-option" title="Remove Option" ${count <= 2 ? 'disabled' : ''}>✕</button>
                `;
                optionsContainer.appendChild(row);
            }

            // Bind remove buttons
            const removeBtns = optionsContainer.querySelectorAll('.btn-remove-option');
            removeBtns.forEach((btn, idx) => {
                btn.addEventListener('click', () => {
                    const currentRows = optionsContainer.querySelectorAll('.question-option-row');
                    if (currentRows.length <= 2) return;
                    const data = [];
                    currentRows.forEach((r, rIdx) => {
                        if (rIdx !== idx) {
                            const inp = r.querySelector('.question-option-input');
                            data.push({ text: inp.value });
                        }
                    });
                    const curSelected = selectCorrectAnswer.value;
                    renderOptionRows(data);
                    const newLabels = optionLabels.slice(0, data.length);
                    updateCorrectAnswerSelect(newLabels, newLabels.includes(curSelected) ? curSelected : '');
                });
            });

            const btnAddOpt = document.getElementById('btnAddOptionRow');
            if (btnAddOpt) btnAddOpt.disabled = count >= 6;
        };

        if (q) {
            document.getElementById('questionModalTitle').textContent = `✏ Edit MCQ Question (Page ${this.currentPageIndex + 1})`;
            inputNum.value = q.questionNumber || `Question ${this.currentPageIndex + 1}`;
            inputText.value = q.questionText || '';
            selectFontSize.value = String(q.fontSize || 26);
            checkReveal.checked = !!q.isAnswerRevealed;
            if (btnDeleteInModal) btnDeleteInModal.style.display = 'inline-block';

            const opts = q.options && q.options.length ? q.options : [{ label: 'A', text: '' }, { label: 'B', text: '' }, { label: 'C', text: '' }, { label: 'D', text: '' }];
            const labels = opts.map((_, i) => optionLabels[i]);
            renderOptionRows(opts);
            updateCorrectAnswerSelect(labels, q.correctAnswer || '');
        } else {
            document.getElementById('questionModalTitle').textContent = `❓ Add MCQ Question (Page ${this.currentPageIndex + 1})`;
            inputNum.value = `Question ${this.currentPageIndex + 1}`;
            inputText.value = '';
            selectFontSize.value = '26';
            checkReveal.checked = false;
            if (btnDeleteInModal) btnDeleteInModal.style.display = 'none';

            const defaultOpts = [{ text: '' }, { text: '' }, { text: '' }, { text: '' }];
            renderOptionRows(defaultOpts);
            updateCorrectAnswerSelect(['A', 'B', 'C', 'D'], '');
        }

        modal.style.display = 'flex';
        setTimeout(() => inputText.focus(), 60);
    }

    bindQuestionEvents() {
        const modal = document.getElementById('questionEditorModal');
        const overlay = document.getElementById('canvasQuestionOverlay');
        const btnAddQuestion = document.getElementById('btnAddQuestion');
        const btnEditQuestion = document.getElementById('btnEditQuestion');
        const btnClose = document.getElementById('btnCloseQuestionModal');
        const btnCancel = document.getElementById('btnCancelQuestionModal');
        const btnSave = document.getElementById('btnSaveQuestionModal');
        const btnDeleteInModal = document.getElementById('btnDeleteQuestionInModal');
        const btnDeleteQuestion = document.getElementById('btnDeleteQuestion');
        const btnToggleReveal = document.getElementById('btnToggleAnswerReveal');
        const btnZoomDec = document.getElementById('btnZoomQuestionDec');
        const btnZoomInc = document.getElementById('btnZoomQuestionInc');
        const btnDrag = document.getElementById('btnDragQuestion');
        const btnAddOptionRow = document.getElementById('btnAddOptionRow');
        const optionsContainer = document.getElementById('questionOptionsContainer');
        const selectCorrectAnswer = document.getElementById('selectCorrectAnswer');

        const optionLabels = ['A', 'B', 'C', 'D', 'E', 'F'];

        // Stop overlay events from triggering drawing canvas strokes
        if (overlay) {
            overlay.addEventListener('pointerdown', (e) => e.stopPropagation());
            overlay.addEventListener('mousedown', (e) => e.stopPropagation());
            overlay.addEventListener('touchstart', (e) => e.stopPropagation());
        }

        const closeModal = () => {
            if (modal) modal.style.display = 'none';
        };

        if (btnAddQuestion) btnAddQuestion.addEventListener('click', () => this.openQuestionModal());
        if (btnEditQuestion) btnEditQuestion.addEventListener('click', () => this.openQuestionModal());
        if (btnClose) btnClose.addEventListener('click', closeModal);
        if (btnCancel) btnCancel.addEventListener('click', closeModal);

        if (btnAddOptionRow) {
            btnAddOptionRow.addEventListener('click', () => {
                const currentRows = optionsContainer.querySelectorAll('.question-option-row');
                if (currentRows.length >= 6) return;
                const data = [];
                currentRows.forEach(r => {
                    const inp = r.querySelector('.question-option-input');
                    data.push({ text: inp ? inp.value : '' });
                });
                data.push({ text: '' });
                const curSelected = selectCorrectAnswer.value;

                // Re-render rows
                optionsContainer.innerHTML = '';
                const count = data.length;
                for (let i = 0; i < count; i++) {
                    const lbl = optionLabels[i];
                    const textVal = data[i] ? data[i].text : '';
                    const row = document.createElement('div');
                    row.className = 'question-option-row';
                    row.dataset.label = lbl;
                    row.innerHTML = `
                        <span class="option-badge-label">${lbl}</span>
                        <input type="text" class="form-control question-option-input" placeholder="Option ${lbl} text..." value="${this.escapeHtml(textVal)}">
                        <button type="button" class="btn btn-xs btn-outline-danger btn-remove-option" title="Remove Option" ${count <= 2 ? 'disabled' : ''}>✕</button>
                    `;
                    optionsContainer.appendChild(row);
                }

                // Bind remove buttons
                optionsContainer.querySelectorAll('.btn-remove-option').forEach((btn, idx) => {
                    btn.addEventListener('click', () => {
                        const rows = optionsContainer.querySelectorAll('.question-option-row');
                        if (rows.length <= 2) return;
                        const remaining = [];
                        rows.forEach((r, rIdx) => {
                            if (rIdx !== idx) {
                                const inp = r.querySelector('.question-option-input');
                                remaining.push({ text: inp.value });
                            }
                        });
                        const curSel = selectCorrectAnswer.value;
                        optionsContainer.innerHTML = '';
                        const newCount = remaining.length;
                        for (let j = 0; j < newCount; j++) {
                            const lbl = optionLabels[j];
                            const textVal = remaining[j] ? remaining[j].text : '';
                            const row = document.createElement('div');
                            row.className = 'question-option-row';
                            row.dataset.label = lbl;
                            row.innerHTML = `
                                <span class="option-badge-label">${lbl}</span>
                                <input type="text" class="form-control question-option-input" placeholder="Option ${lbl} text..." value="${this.escapeHtml(textVal)}">
                                <button type="button" class="btn btn-xs btn-outline-danger btn-remove-option" title="Remove Option" ${newCount <= 2 ? 'disabled' : ''}>✕</button>
                            `;
                            optionsContainer.appendChild(row);
                        }
                        const newLabels = optionLabels.slice(0, newCount);
                        selectCorrectAnswer.innerHTML = '<option value="">None (Ungraded / Poll)</option>';
                        newLabels.forEach(lbl => {
                            const opt = document.createElement('option');
                            opt.value = lbl;
                            opt.textContent = `Option ${lbl}`;
                            if (lbl === curSel) opt.selected = true;
                            selectCorrectAnswer.appendChild(opt);
                        });
                        btnAddOptionRow.disabled = newCount >= 6;
                    });
                });

                const newLabels = optionLabels.slice(0, count);
                selectCorrectAnswer.innerHTML = '<option value="">None (Ungraded / Poll)</option>';
                newLabels.forEach(lbl => {
                    const opt = document.createElement('option');
                    opt.value = lbl;
                    opt.textContent = `Option ${lbl}`;
                    if (lbl === curSelected) opt.selected = true;
                    selectCorrectAnswer.appendChild(opt);
                });
                btnAddOptionRow.disabled = count >= 6;
            });
        }

        const deleteQuestion = () => {
            const prev = this.currentPage.question;
            if (!prev) return;
            this.currentPage.question = null;
            this.undoStack.push({
                type: 'deleteQuestion',
                prevQuestion: prev,
                pageIndex: this.currentPageIndex
            });
            this.redoStack = [];
            closeModal();
            this.renderCanvas();
            this.syncQuestionOverlay();
        };

        if (btnDeleteInModal) {
            btnDeleteInModal.addEventListener('click', () => {
                if (confirm('Are you sure you want to remove the question from this page?')) {
                    deleteQuestion();
                }
            });
        }

        if (btnDeleteQuestion) {
            btnDeleteQuestion.addEventListener('click', () => {
                if (confirm('Are you sure you want to remove the question from this page?')) {
                    deleteQuestion();
                }
            });
        }

        if (btnSave) {
            btnSave.addEventListener('click', () => {
                const inputNum = document.getElementById('inputQuestionNumber');
                const inputText = document.getElementById('inputQuestionText');
                const selectFontSize = document.getElementById('selectQuestionFontSize');
                const checkReveal = document.getElementById('checkRevealAnswerOnSave');

                const text = (inputText.value || '').trim();
                if (!text) {
                    alert('Please enter the question text or prompt.');
                    inputText.focus();
                    return;
                }

                const rows = optionsContainer.querySelectorAll('.question-option-row');
                const options = [];
                rows.forEach((r, idx) => {
                    const lbl = optionLabels[idx] || `Option ${idx + 1}`;
                    const val = r.querySelector('.question-option-input').value.trim();
                    options.push({ label: lbl, text: val });
                });

                const prev = this.currentPage.question ? { ...this.currentPage.question } : null;
                const newQ = {
                    id: prev?.id || this.generateGuid(),
                    questionNumber: inputNum.value.trim(),
                    questionText: text,
                    options: options,
                    correctAnswer: selectCorrectAnswer.value || '',
                    isAnswerRevealed: checkReveal.checked,
                    x: prev?.x ?? 80,
                    y: prev?.y ?? 80,
                    width: prev?.width ?? 1000,
                    fontSize: parseInt(selectFontSize.value, 10) || 26,
                    fontFamily: '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif'
                };

                this.currentPage.question = newQ;
                this.undoStack.push({
                    type: 'setQuestion',
                    prevQuestion: prev,
                    nextQuestion: { ...newQ },
                    pageIndex: this.currentPageIndex
                });
                this.redoStack = [];

                closeModal();
                this.renderCanvas();
                this.syncQuestionOverlay();
            });
        }

        // Toggle Answer Reveal
        if (btnToggleReveal) {
            btnToggleReveal.addEventListener('click', () => {
                const q = this.currentPage?.question;
                if (!q) return;
                q.isAnswerRevealed = !q.isAnswerRevealed;
                this.renderCanvas();
                this.syncQuestionOverlay();
            });
        }

        // Font Zoom
        if (btnZoomDec) {
            btnZoomDec.addEventListener('click', () => {
                const q = this.currentPage?.question;
                if (!q) return;
                q.fontSize = Math.max(16, (q.fontSize || 26) - 2);
                this.renderCanvas();
            });
        }
        if (btnZoomInc) {
            btnZoomInc.addEventListener('click', () => {
                const q = this.currentPage?.question;
                if (!q) return;
                q.fontSize = Math.min(42, (q.fontSize || 26) + 2);
                this.renderCanvas();
            });
        }

        // Drag Overlay / Reposition Question
        if (btnDrag) {
            let isDragging = false;
            let startClientX = 0;
            let startClientY = 0;
            let startQx = 0;
            let startQy = 0;

            btnDrag.addEventListener('pointerdown', (e) => {
                e.stopPropagation();
                const q = this.currentPage?.question;
                if (!q) return;
                isDragging = true;
                btnDrag.setPointerCapture(e.pointerId);
                startClientX = e.clientX;
                startClientY = e.clientY;
                startQx = q.x || 80;
                startQy = q.y || 80;
                e.preventDefault();
            });

            btnDrag.addEventListener('pointermove', (e) => {
                if (!isDragging) return;
                e.stopPropagation();
                const q = this.currentPage?.question;
                if (!q) return;

                const scaleX = this.cssWidth / 1920;
                const scaleY = this.cssHeight / 1080;
                const dx = (e.clientX - startClientX) / scaleX;
                const dy = (e.clientY - startClientY) / scaleY;

                q.x = Math.round(Math.max(20, Math.min(1920 - (q.width || 1000) - 20, startQx + dx)));
                q.y = Math.round(Math.max(20, Math.min(1080 - (q._cardHeight || 300) - 20, startQy + dy)));

                this.renderCanvas();
            });

            const endDrag = (e) => {
                if (isDragging) {
                    isDragging = false;
                    try { btnDrag.releasePointerCapture(e.pointerId); } catch { }
                }
            };

            btnDrag.addEventListener('pointerup', endDrag);
            btnDrag.addEventListener('pointercancel', endDrag);
        }
    }

    // ==========================================
    // 📥 Question / MCQ Import & Extraction Engine
    // ==========================================
    openQuestionImportModal() {
        const modal = document.getElementById('questionImportModal');
        if (!modal) return;
        const progressBox = document.getElementById('questionImportProgressContainer');
        const progressBar = document.getElementById('questionImportProgressBar');
        const statusText = document.getElementById('questionImportStatusText');
        const fileInput = document.getElementById('questionImportFileInput');

        if (progressBox) progressBox.style.display = 'none';
        if (progressBar) progressBar.style.width = '0%';
        if (statusText) statusText.textContent = 'Ready to extract questions...';
        if (fileInput) fileInput.value = '';

        modal.style.display = 'flex';
    }

    bindQuestionImportEvents() {
        const importModal = document.getElementById('questionImportModal');
        const reviewModal = document.getElementById('questionReviewModal');
        const btnImport = document.getElementById('btnImportQuestions');
        const btnToolImport = document.getElementById('btnToolImportQuestion');
        const btnCloseImport = document.getElementById('btnCloseQuestionImportModal');
        const btnCancelImport = document.getElementById('btnCancelQuestionImport');
        const btnCloseReview = document.getElementById('btnCloseQuestionReviewModal');
        const btnCancelReview = document.getElementById('btnCancelQuestionReview');
        const dropZone = document.getElementById('questionDropZone');
        const fileInput = document.getElementById('questionImportFileInput');
        const btnBrowse = document.getElementById('btnBrowseQuestionFiles');
        const btnSelectAll = document.getElementById('btnSelectAllReviewQuestions');
        const btnDeselectAll = document.getElementById('btnDeselectAllReviewQuestions');
        const btnAddManual = document.getElementById('btnAddManualReviewQuestion');
        const btnConfirmImport = document.getElementById('btnConfirmQuestionImport');

        if (btnImport) btnImport.addEventListener('click', () => this.openQuestionImportModal());
        if (btnToolImport) btnToolImport.addEventListener('click', () => this.openQuestionImportModal());

        const closeImport = () => {
            if (importModal) importModal.style.display = 'none';
        };
        const closeReview = () => {
            if (reviewModal) reviewModal.style.display = 'none';
        };

        if (btnCloseImport) btnCloseImport.addEventListener('click', closeImport);
        if (btnCancelImport) btnCancelImport.addEventListener('click', closeImport);
        if (btnCloseReview) btnCloseReview.addEventListener('click', closeReview);
        if (btnCancelReview) btnCancelReview.addEventListener('click', closeReview);

        if (btnBrowse && fileInput) {
            btnBrowse.addEventListener('click', () => fileInput.click());
        }

        // File input change handler
        if (fileInput) {
            fileInput.addEventListener('change', async (e) => {
                const files = Array.from(e.target.files || []);
                if (files.length === 0) return;
                await this.handleQuestionFilesUpload(files);
                fileInput.value = '';
            });
        }

        // Drag and Drop handlers
        if (dropZone) {
            dropZone.addEventListener('dragover', (e) => {
                e.preventDefault();
                e.stopPropagation();
                dropZone.classList.add('drag-over');
            });
            dropZone.addEventListener('dragleave', (e) => {
                e.preventDefault();
                e.stopPropagation();
                dropZone.classList.remove('drag-over');
            });
            dropZone.addEventListener('drop', async (e) => {
                e.preventDefault();
                e.stopPropagation();
                dropZone.classList.remove('drag-over');
                if (e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files.length > 0) {
                    await this.handleQuestionFilesUpload(Array.from(e.dataTransfer.files));
                }
            });
        }
    }

    async handleQuestionFilesUpload(files) {
        const progressBox = document.getElementById('questionImportProgressContainer');
        const progressBar = document.getElementById('questionImportProgressBar');
        const statusText = document.getElementById('questionImportStatusText');
        const percentText = document.getElementById('questionImportPercentText');

        if (progressBox) progressBox.style.display = 'block';

        const updateProgress = (text, pct) => {
            if (statusText) statusText.textContent = text;
            if (progressBar) progressBar.style.width = `${pct}%`;
            if (percentText) percentText.textContent = `${pct}%`;
        };

        try {
            updateProgress('Reading files and extracting content...', 15);
            const extractedQuestions = await this.extractQuestionsFromFiles(files, updateProgress);
            updateProgress('Finalizing question structure...', 90);

            await new Promise(r => setTimeout(r, 300));
            updateProgress('Done!', 100);

            // Close import modal and open review modal
            const importModal = document.getElementById('questionImportModal');
            if (importModal) importModal.style.display = 'none';

            this.openQuestionReviewModal(extractedQuestions);
        } catch (err) {
            console.error('Extraction error:', err);
            alert(`Error extracting questions from file: ${err.message || err}`);
            if (progressBox) progressBox.style.display = 'none';
        }
    }

    async extractQuestionsFromFiles(files, onProgress) {
        const allQuestions = [];
        const totalFiles = files.length;

        for (let i = 0; i < totalFiles; i++) {
            const file = files[i];
            const ext = file.name.split('.').pop().toLowerCase();
            const basePct = Math.round((i / totalFiles) * 80);

            if (onProgress) {
                onProgress(`Processing ${file.name} (${i + 1}/${totalFiles})...`, basePct + 5);
            }

            if (['pptx', 'ppsx', 'pptm', 'potx', 'odp'].includes(ext)) {
                const qs = await this.extractQuestionsFromPptx(file);
                allQuestions.push(...qs);
            } else if (ext === 'ppt') {
                const qs = await this.extractQuestionsFromPpt(file);
                allQuestions.push(...qs);
            } else if (ext === 'pdf') {
                const qs = await this.extractQuestionsFromPdf(file, onProgress);
                allQuestions.push(...qs);
            } else if (file.type.startsWith('image/') || ['png', 'jpg', 'jpeg', 'webp', 'bmp'].includes(ext)) {
                const qs = await this.extractQuestionsFromImage(file, onProgress);
                allQuestions.push(...qs);
            } else if (file.type.startsWith('text/') || ['txt', 'md', 'json', 'csv'].includes(ext)) {
                const text = await new Promise(r => {
                    const reader = new FileReader();
                    reader.onload = () => r(reader.result);
                    reader.onerror = () => r('');
                    reader.readAsText(file);
                });
                const qs = this.parseQuestionsFromText(text, file.name);
                allQuestions.push(...qs);
            } else {
                // Generic fallback
                const qs = this.parseQuestionsFromText(`Question from ${file.name}\n\nA. Option A\nB. Option B\nC. Option C\nD. Option D`, file.name);
                allQuestions.push(...qs);
            }
        }

        // Re-index auto numbers if needed
        allQuestions.forEach((q, idx) => {
            if (!q.questionNumber || q.questionNumber.startsWith('Question ')) {
                q.questionNumber = `Question ${idx + 1}`;
            }
        });

        return allQuestions;
    }

    async extractQuestionsFromPptx(file) {
        const questions = [];
        if (typeof JSZip === 'undefined') {
            await new Promise(r => setTimeout(r, 200));
        }
        const zip = await JSZip.loadAsync(await file.arrayBuffer());
        const parser = new DOMParser();

        const slideFiles = Object.keys(zip.files)
            .filter(name => /^ppt\/slides\/slide\d+\.xml$/i.test(name))
            .sort((a, b) => {
                const numA = parseInt(a.match(/\d+/)[0], 10);
                const numB = parseInt(b.match(/\d+/)[0], 10);
                return numA - numB;
            });

        for (let i = 0; i < slideFiles.length; i++) {
            const slideFileName = slideFiles[i];
            const slideNum = i + 1;
            const slideXmlStr = await zip.file(slideFileName).async('string');
            const slideDoc = parser.parseFromString(slideXmlStr, 'application/xml');

            const paragraphs = [];
            const pNodes = slideDoc.getElementsByTagName('a:p');
            for (let p = 0; p < pNodes.length; p++) {
                const tNodes = pNodes[p].getElementsByTagName('a:t');
                const line = Array.from(tNodes).map(t => t.textContent).join('').trim();
                if (line) paragraphs.push(line);
            }

            // Extract notes
            let notesText = '';
            const notesFile = zip.file(`ppt/notesSlides/notesSlide${slideNum}.xml`);
            if (notesFile) {
                try {
                    const notesXml = await notesFile.async('string');
                    const notesDoc = parser.parseFromString(notesXml, 'application/xml');
                    const nNodes = notesDoc.getElementsByTagName('a:t');
                    notesText = Array.from(nNodes).map(t => t.textContent).join(' ').trim();
                } catch { }
            }

            const slideFullText = paragraphs.join('\n') + (notesText ? '\n' + notesText : '');
            const parsed = this.parseQuestionsFromText(slideFullText, `${file.name} (Slide ${slideNum})`);
            if (parsed.length > 0) {
                questions.push(...parsed);
            }
        }

        return questions;
    }

    async extractQuestionsFromPpt(file) {
        const questions = [];
        try {
            const buffer = await file.arrayBuffer();
            const bytes = new Uint8Array(buffer);
            let asciiRuns = [];
            let currentRun = '';

            for (let i = 0; i < bytes.length; i++) {
                const b = bytes[i];
                if ((b >= 32 && b <= 126) || b === 10 || b === 13 || b === 9) {
                    currentRun += String.fromCharCode(b);
                } else {
                    if (currentRun.trim().length >= 3) {
                        asciiRuns.push(currentRun.trim());
                    }
                    currentRun = '';
                }
            }
            if (currentRun.trim().length >= 3) asciiRuns.push(currentRun.trim());

            const fullText = asciiRuns.join('\n');
            const parsed = this.parseQuestionsFromText(fullText, file.name);
            if (parsed.length > 0) return parsed;
        } catch (e) {
            console.warn('PPT binary parse fallback:', e);
        }

        questions.push({
            id: this.generateGuid(),
            source: file.name,
            questionNumber: 'Question 1',
            questionText: `PowerPoint Presentation (${file.name})`,
            options: [
                { label: 'A', text: 'Option A' },
                { label: 'B', text: 'Option B' },
                { label: 'C', text: 'Option C' },
                { label: 'D', text: 'Option D' }
            ],
            correctAnswer: '',
            selected: true,
            hasWarning: true,
            warningText: 'Binary PPT format extracted with basic text scanner. Please review.',
            fontSize: 26
        });
        return questions;
    }

    async extractQuestionsFromPdf(file, onProgress) {
        const questions = [];
        const arrayBuffer = await file.arrayBuffer();

        // 1. Try PDF.js if available or dynamic load from CDN
        try {
            if (typeof pdfjsLib === 'undefined') {
                const script = document.createElement('script');
                script.src = 'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.11.174/pdf.min.js';
                document.head.appendChild(script);
                await new Promise((resolve, reject) => {
                    script.onload = resolve;
                    script.onerror = reject;
                    setTimeout(reject, 3000);
                });
                if (typeof pdfjsLib !== 'undefined') {
                    pdfjsLib.GlobalWorkerOptions.workerSrc = 'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.11.174/pdf.worker.min.js';
                }
            }

            if (typeof pdfjsLib !== 'undefined') {
                const pdf = await pdfjsLib.getDocument({ data: arrayBuffer }).promise;
                for (let p = 1; p <= pdf.numPages; p++) {
                    if (onProgress) {
                        onProgress(`Extracting PDF page ${p} of ${pdf.numPages}...`, Math.round((p / pdf.numPages) * 70) + 15);
                    }
                    const page = await pdf.getPage(p);
                    const textContent = await page.getTextContent();
                    let lastY = null;
                    let pageText = '';
                    for (const item of textContent.items) {
                        if (lastY !== null && Math.abs(item.transform[5] - lastY) > 5) {
                            pageText += '\n';
                        } else if (pageText && !pageText.endsWith(' ') && !pageText.endsWith('\n')) {
                            pageText += ' ';
                        }
                        pageText += item.str;
                        lastY = item.transform[5];
                    }
                    const parsed = this.parseQuestionsFromText(pageText, `${file.name} (Page ${p})`);
                    questions.push(...parsed);
                }
                if (questions.length > 0) return questions;
            }
        } catch (e) {
            console.warn('PDF.js dynamic load failed or offline, falling back to stream decoder...', e);
        }

        // 2. Pure JS stream decoder fallback
        const bytes = new Uint8Array(arrayBuffer);
        let rawStr = '';
        for (let i = 0; i < bytes.length; i++) {
            rawStr += String.fromCharCode(bytes[i]);
        }

        const matches = rawStr.match(/\(([^)]+)\)\s*Tj/g) || rawStr.match(/\[(.*?)\]\s*TJ/g);
        if (matches && matches.length > 0) {
            const extracted = matches.map(m => m.replace(/^[(\[]|[)\]]\s*T[jJ]$/g, '').trim()).join('\n');
            const parsed = this.parseQuestionsFromText(extracted, file.name);
            questions.push(...parsed);
        }

        if (questions.length === 0) {
            questions.push({
                id: this.generateGuid(),
                source: file.name,
                questionNumber: 'Question 1',
                questionText: `PDF Document: ${file.name}. Please enter question prompt.`,
                options: [
                    { label: 'A', text: 'Option A' },
                    { label: 'B', text: 'Option B' },
                    { label: 'C', text: 'Option C' },
                    { label: 'D', text: 'Option D' }
                ],
                correctAnswer: '',
                selected: true,
                hasWarning: true,
                warningText: 'PDF text could not be extracted directly. Please review.',
                fontSize: 26
            });
        }

        return questions;
    }

    async extractQuestionsFromImage(file, onProgress) {
        const questions = [];
        try {
            if (typeof Tesseract === 'undefined') {
                const script = document.createElement('script');
                script.src = 'https://cdn.jsdelivr.net/npm/tesseract.js@5/dist/tesseract.min.js';
                document.head.appendChild(script);
                await new Promise((resolve, reject) => {
                    script.onload = resolve;
                    script.onerror = reject;
                    setTimeout(reject, 4000);
                });
            }

            if (typeof Tesseract !== 'undefined') {
                const worker = await Tesseract.createWorker('eng', 1, {
                    logger: m => {
                        if (m && m.status && onProgress) {
                            const pct = Math.round((m.progress || 0) * 100);
                            onProgress(`OCR Recognizing: ${m.status}... (${pct}%)`, Math.round(pct * 0.7) + 20);
                        }
                    }
                });

                const ret = await worker.recognize(file);
                await worker.terminate();

                const text = ret.data.text;
                const parsed = this.parseQuestionsFromText(text, file.name);
                if (parsed.length > 0) {
                    return parsed;
                }
            }
        } catch (e) {
            console.warn('OCR error or offline:', e);
        }

        // Fallback for image
        questions.push({
            id: this.generateGuid(),
            source: file.name,
            questionNumber: 'Question 1',
            questionText: `Image: ${file.name}. Review question and options extracted.`,
            options: [
                { label: 'A', text: '' },
                { label: 'B', text: '' },
                { label: 'C', text: '' },
                { label: 'D', text: '' }
            ],
            correctAnswer: '',
            selected: true,
            hasWarning: true,
            warningText: 'OCR could not automatically detect all text. Please edit.',
            fontSize: 26
        });
        return questions;
    }

    parseQuestionsFromText(rawText, source = '') {
        const results = [];
        if (!rawText || !rawText.trim()) return results;

        const lines = rawText.split(/\r?\n/).map(l => l.trim());
        let current = null;
        let promptLines = [];
        let inOptions = false;
        let optionType = null;
        let hasAnswer = false;
        let autoNumber = 1;

        const explicitQuestionHeaderRegex = /^(?:(?:Question|Que|Ques|Prob|Problem|MCQ)\s*[:.#-]?\s*(\d+|[A-Za-z]+)\b(?:\s*[:.-])?|Q\s*[:.#-]?\s*(\d+)\b(?:\s*[:.-])?)\s*(.*)$/i;
        const bareNumberedQuestionRegex = /^(\d+)[\.\)]\s+(.*)$/;
        const letterOptionRegex = /^(?:\(?\s*([A-Fa-f])\s*[\.\)\:\-]\s*|\(\s*([A-Fa-f])\s*\)\s*|\[\s*([A-Fa-f])\s*\]\s*)(.*)$/;
        const numericOptionRegex = /^(?:\(?\s*([1-6])\s*[\.\)\:\-]\s*|\(\s*([1-6])\s*\)\s*|\[\s*([1-6])\s*\]\s*)(.*)$/;
        const answerRegex = /^(?:Correct\s*Answer|Answer|Ans|Key|Correct\s*Option)\s*[:=\-]?\s*(?:\(\s*([A-Fa-f1-6])\s*\)|\[\s*([A-Fa-f1-6])\s*\]|([A-Fa-f1-6]))/i;

        const commitCurrent = () => {
            if (current) {
                current.questionText = promptLines.join('\n').trim();
                if (!current.questionNumber) {
                    current.questionNumber = `Question ${autoNumber++}`;
                }
                if (!current.options || current.options.length < 2) {
                    current.hasWarning = true;
                    current.warningText = 'Could not identify 2 or more options. Please review.';
                    if (!current.options) current.options = [];
                    while (current.options.length < 2) {
                        current.options.push({
                            label: String.fromCharCode(65 + current.options.length),
                            text: ''
                        });
                    }
                }
                if (current.questionText || current.options.some(o => o.text)) {
                    results.push(current);
                }
            }
            current = null;
            promptLines = [];
            inOptions = false;
            optionType = null;
            hasAnswer = false;
        };

        for (const line of lines) {
            if (!line) continue;

            // 1. Answer check
            const ansMatch = line.match(answerRegex);
            if (ansMatch && current) {
                let ansVal = (ansMatch[1] || ansMatch[2] || ansMatch[3] || '').toUpperCase();
                if (ansVal) {
                    const numVal = parseInt(ansVal, 10);
                    if (!isNaN(numVal) && numVal >= 1 && numVal <= 6) {
                        ansVal = String.fromCharCode(65 + numVal - 1);
                    }
                    current.correctAnswer = ansVal;
                }
                hasAnswer = true;
                continue;
            }

            // 2. Explicit Question Header Check (e.g. "Question 1", "Q1.", "MCQ 1:")
            const explicitQMatch = line.match(explicitQuestionHeaderRegex);
            if (explicitQMatch) {
                commitCurrent();
                current = {
                    id: this.generateGuid(),
                    source: source,
                    options: [],
                    correctAnswer: '',
                    selected: true,
                    fontSize: 26
                };
                const numPart = explicitQMatch[1] || explicitQMatch[2];
                current.questionNumber = numPart ? `Question ${numPart}` : `Question ${autoNumber++}`;
                const promptPart = (explicitQMatch[3] || '').trim();
                if (promptPart) promptLines.push(promptPart);
                continue;
            }

            // 3. Option letter check (e.g. "A. Option", "(b) Option")
            const letterOptMatch = line.match(letterOptionRegex);
            if (letterOptMatch) {
                if (!current) {
                    current = {
                        id: this.generateGuid(),
                        source: source,
                        questionNumber: `Question ${autoNumber++}`,
                        options: [],
                        correctAnswer: '',
                        selected: true,
                        fontSize: 26
                    };
                }
                inOptions = true;
                optionType = 'letter';
                const label = (letterOptMatch[1] || letterOptMatch[2] || letterOptMatch[3] || 'A').toUpperCase();
                const optText = (letterOptMatch[4] || '').trim();
                current.options.push({ label, text: optText });
                continue;
            }

            // 4. Numeric Option Check (1..6)
            const numOptMatch = line.match(numericOptionRegex);
            if (numOptMatch) {
                const optNum = parseInt(numOptMatch[1] || numOptMatch[2] || numOptMatch[3], 10);
                if (!isNaN(optNum) && optNum >= 1 && optNum <= 6) {
                    if (inOptions && optionType === 'numeric') {
                        const label = String.fromCharCode(65 + optNum - 1);
                        const optText = (numOptMatch[4] || '').trim();
                        current.options.push({ label, text: optText });
                        continue;
                    }

                    if (current && !inOptions && promptLines.length > 0 && !hasAnswer) {
                        inOptions = true;
                        optionType = 'numeric';
                        const label = String.fromCharCode(65 + optNum - 1);
                        const optText = (numOptMatch[4] || '').trim();
                        current.options.push({ label, text: optText });
                        continue;
                    }
                }
            }

            // 5. Bare Numbered Question Check (e.g. "1. What is...", "2. What is...")
            const bareQMatch = line.match(bareNumberedQuestionRegex);
            if (bareQMatch && (!current || hasAnswer || (inOptions && optionType === 'letter'))) {
                commitCurrent();
                current = {
                    id: this.generateGuid(),
                    source: source,
                    questionNumber: `Question ${bareQMatch[1]}`,
                    options: [],
                    correctAnswer: '',
                    selected: true,
                    fontSize: 26
                };
                const promptPart = (bareQMatch[2] || '').trim();
                if (promptPart) promptLines.push(promptPart);
                continue;
            }

            // 6. Continuation of Option or Prompt
            if (current) {
                if (inOptions && current.options.length > 0) {
                    current.options[current.options.length - 1].text += ' ' + line;
                } else {
                    promptLines.push(line);
                }
            } else {
                current = {
                    id: this.generateGuid(),
                    source: source,
                    options: [],
                    correctAnswer: '',
                    selected: true,
                    fontSize: 26
                };
                promptLines.push(line);
            }
        }

        commitCurrent();

        if (results.length === 0 && lines.length > 0) {
            results.push({
                id: this.generateGuid(),
                source: source,
                questionNumber: `Question 1`,
                questionText: lines.slice(0, 8).join('\n'),
                options: [
                    { label: 'A', text: '' },
                    { label: 'B', text: '' },
                    { label: 'C', text: '' },
                    { label: 'D', text: '' }
                ],
                correctAnswer: '',
                selected: true,
                hasWarning: true,
                warningText: 'Could not automatically identify options. Please edit.',
                fontSize: 26
            });
        }

        return results;
    }

    openQuestionReviewModal(questionsList) {
        const reviewModal = document.getElementById('questionReviewModal');
        if (!reviewModal) return;

        const container = document.getElementById('reviewQuestionsContainer');
        const statsEl = document.getElementById('reviewQuestionStats');
        const countEl = document.getElementById('reviewSelectionCount');
        const btnSelectAll = document.getElementById('btnSelectAllReviewQuestions');
        const btnDeselectAll = document.getElementById('btnDeselectAllReviewQuestions');
        const btnAddManual = document.getElementById('btnAddManualReviewQuestion');
        const btnConfirm = document.getElementById('btnConfirmQuestionImport');

        let questions = [...(questionsList || [])];
        const optionLabels = ['A', 'B', 'C', 'D', 'E', 'F'];

        const updateStatsAndCount = () => {
            const total = questions.length;
            const selected = questions.filter(q => q.selected).length;
            const clean = questions.filter(q => !q.hasWarning).length;
            const warn = questions.filter(q => q.hasWarning).length;

            if (statsEl) {
                statsEl.innerHTML = `Found <b>${total}</b> questions (${clean} detected cleanly, ${warn > 0 ? `<span style="color:#FBBF24;">${warn} need review</span>` : '0 issues'})`;
            }
            if (countEl) {
                countEl.textContent = `${selected} of ${total} questions selected`;
            }
            if (btnConfirm) {
                btnConfirm.textContent = `📥 Import ${selected} Selected Question${selected === 1 ? '' : 's'}`;
                btnConfirm.disabled = selected === 0;
            }
        };

        const renderCards = () => {
            container.innerHTML = '';

            questions.forEach((q, qIdx) => {
                const card = document.createElement('div');
                card.className = `review-question-card ${q.selected ? '' : 'deselected'} ${q.hasWarning ? 'has-warning' : ''}`;
                card.dataset.id = q.id;

                const options = q.options && q.options.length ? q.options : [
                    { label: 'A', text: '' }, { label: 'B', text: '' }, { label: 'C', text: '' }, { label: 'D', text: '' }
                ];

                let optionsHtml = '';
                options.forEach((opt, optIdx) => {
                    const lbl = optionLabels[optIdx] || opt.label || String.fromCharCode(65 + optIdx);
                    const isCorrect = (q.correctAnswer || '').toUpperCase() === lbl.toUpperCase();
                    optionsHtml += `
                        <div class="review-option-row" data-opt-idx="${optIdx}">
                            <div class="review-option-badge ${isCorrect ? 'is-correct' : ''}">${lbl}</div>
                            <input type="text" class="form-control form-control-sm review-opt-input" placeholder="Option ${lbl} text..." value="${this.escapeHtml(opt.text || '')}">
                            <button type="button" class="btn btn-xs btn-outline-danger btn-remove-review-opt" title="Remove Option" ${options.length <= 2 ? 'disabled' : ''}>✕</button>
                        </div>
                    `;
                });

                let answerSelectOptions = '<option value="">None (Ungraded / Poll)</option>';
                options.forEach((opt, optIdx) => {
                    const lbl = optionLabels[optIdx] || opt.label;
                    answerSelectOptions += `<option value="${lbl}" ${q.correctAnswer === lbl ? 'selected' : ''}>Option ${lbl}</option>`;
                });

                card.innerHTML = `
                    <div class="review-card-header">
                        <div class="review-card-title-group">
                            <input type="checkbox" class="review-q-select" ${q.selected ? 'checked' : ''} style="width:16px; height:16px; cursor:pointer;">
                            <input type="text" class="form-control form-control-sm review-q-num" value="${this.escapeHtml(q.questionNumber || `Question ${qIdx + 1}`)}" style="width:160px; font-weight:600;">
                            ${q.hasWarning ? `<span class="review-warning-badge" title="${this.escapeHtml(q.warningText || 'Please review')}">⚠ Review Needed</span>` : ''}
                            ${q.source ? `<span style="font-size:11px; color:#64748B;">(${this.escapeHtml(q.source)})</span>` : ''}
                        </div>
                        <div class="review-card-actions">
                            <button type="button" class="btn btn-xs btn-dark btn-move-up-q" title="Move Up" ${qIdx === 0 ? 'disabled' : ''}>▲</button>
                            <button type="button" class="btn btn-xs btn-dark btn-move-down-q" title="Move Down" ${qIdx === questions.length - 1 ? 'disabled' : ''}>▼</button>
                            <button type="button" class="btn btn-xs btn-outline-danger btn-del-review-q" title="Delete Question">🗑</button>
                        </div>
                    </div>
                    <div class="review-card-body">
                        <textarea class="form-control review-q-text" rows="2" placeholder="Question statement / prompt...">${this.escapeHtml(q.questionText || '')}</textarea>
                        <div class="review-options-container">
                            ${optionsHtml}
                        </div>
                        <div class="review-bottom-row">
                            <button type="button" class="btn btn-xs btn-secondary btn-add-review-opt" ${options.length >= 6 ? 'disabled' : ''}>+ Option</button>
                            <div style="display:flex; align-items:center; gap:8px;">
                                <label style="font-size:12px; color:#94A3B8; margin-bottom:0;">Correct Answer:</label>
                                <select class="form-select form-select-sm review-correct-select" style="width:160px;">
                                    ${answerSelectOptions}
                                </select>
                            </div>
                        </div>
                    </div>
                `;

                // Bind events for this card
                const chk = card.querySelector('.review-q-select');
                chk.addEventListener('change', (e) => {
                    q.selected = e.target.checked;
                    card.classList.toggle('deselected', !q.selected);
                    updateStatsAndCount();
                });

                const numInp = card.querySelector('.review-q-num');
                numInp.addEventListener('input', (e) => {
                    q.questionNumber = e.target.value;
                });

                const textInp = card.querySelector('.review-q-text');
                textInp.addEventListener('input', (e) => {
                    q.questionText = e.target.value;
                });

                const correctSel = card.querySelector('.review-correct-select');
                correctSel.addEventListener('change', (e) => {
                    q.correctAnswer = e.target.value;
                    // update option badge colors
                    const badges = card.querySelectorAll('.review-option-badge');
                    badges.forEach((b, idx) => {
                        const lbl = optionLabels[idx];
                        b.classList.toggle('is-correct', q.correctAnswer === lbl);
                    });
                });

                // Option inputs
                const optInputs = card.querySelectorAll('.review-opt-input');
                optInputs.forEach((inp, idx) => {
                    inp.addEventListener('input', (e) => {
                        if (q.options[idx]) q.options[idx].text = e.target.value;
                    });
                });

                // Remove option buttons
                const removeBtns = card.querySelectorAll('.btn-remove-review-opt');
                removeBtns.forEach((b, idx) => {
                    b.addEventListener('click', () => {
                        if (q.options.length <= 2) return;
                        q.options.splice(idx, 1);
                        renderCards();
                        updateStatsAndCount();
                    });
                });

                // Add option button
                const btnAddOpt = card.querySelector('.btn-add-review-opt');
                if (btnAddOpt) {
                    btnAddOpt.addEventListener('click', () => {
                        if (q.options.length >= 6) return;
                        const nextLabel = optionLabels[q.options.length] || String.fromCharCode(65 + q.options.length);
                        q.options.push({ label: nextLabel, text: '' });
                        renderCards();
                        updateStatsAndCount();
                    });
                }

                // Delete question
                const btnDelQ = card.querySelector('.btn-del-review-q');
                if (btnDelQ) {
                    btnDelQ.addEventListener('click', () => {
                        questions.splice(qIdx, 1);
                        renderCards();
                        updateStatsAndCount();
                    });
                }

                // Move up / down
                const btnUp = card.querySelector('.btn-move-up-q');
                if (btnUp) {
                    btnUp.addEventListener('click', () => {
                        if (qIdx > 0) {
                            const tmp = questions[qIdx];
                            questions[qIdx] = questions[qIdx - 1];
                            questions[qIdx - 1] = tmp;
                            renderCards();
                            updateStatsAndCount();
                        }
                    });
                }

                const btnDown = card.querySelector('.btn-move-down-q');
                if (btnDown) {
                    btnDown.addEventListener('click', () => {
                        if (qIdx < questions.length - 1) {
                            const tmp = questions[qIdx];
                            questions[qIdx] = questions[qIdx + 1];
                            questions[qIdx + 1] = tmp;
                            renderCards();
                            updateStatsAndCount();
                        }
                    });
                }

                container.appendChild(card);
            });
        };

        if (btnSelectAll) {
            btnSelectAll.onclick = () => {
                questions.forEach(q => q.selected = true);
                renderCards();
                updateStatsAndCount();
            };
        }

        if (btnDeselectAll) {
            btnDeselectAll.onclick = () => {
                questions.forEach(q => q.selected = false);
                renderCards();
                updateStatsAndCount();
            };
        }

        if (btnAddManual) {
            btnAddManual.onclick = () => {
                const nextNum = questions.length + 1;
                questions.push({
                    id: this.generateGuid(),
                    questionNumber: `Question ${nextNum}`,
                    questionText: '',
                    options: [
                        { label: 'A', text: '' },
                        { label: 'B', text: '' },
                        { label: 'C', text: '' },
                        { label: 'D', text: '' }
                    ],
                    correctAnswer: '',
                    selected: true,
                    fontSize: 26
                });
                renderCards();
                updateStatsAndCount();
                // Scroll to bottom
                const reviewBody = document.querySelector('.question-review-body');
                if (reviewBody) reviewBody.scrollTop = reviewBody.scrollHeight;
            };
        }

        if (btnConfirm) {
            btnConfirm.onclick = () => {
                const selected = questions.filter(q => q.selected);
                if (selected.length === 0) {
                    alert('Please select at least one question to import.');
                    return;
                }

                const modeRadio = document.querySelector('input[name="importPlacementMode"]:checked');
                const mode = modeRadio ? modeRadio.value : 'new-pages';

                this.importQuestionsIntoStudio(selected, mode);
                reviewModal.style.display = 'none';
            };
        }

        renderCards();
        updateStatsAndCount();
        reviewModal.style.display = 'flex';
    }

    importQuestionsIntoStudio(selectedQuestions, mode = 'new-pages') {
        if (!selectedQuestions || selectedQuestions.length === 0) return;

        const prevPages = JSON.parse(JSON.stringify(this.pages));
        const prevPageIndex = this.currentPageIndex;

        if (mode === 'current-page') {
            const first = selectedQuestions[0];
            const qObj = {
                id: this.generateGuid(),
                questionNumber: first.questionNumber || `Question ${this.currentPageIndex + 1}`,
                questionText: first.questionText || '',
                options: (first.options || []).map((opt, i) => ({
                    label: opt.label || String.fromCharCode(65 + i),
                    text: opt.text || ''
                })),
                correctAnswer: first.correctAnswer || '',
                isAnswerRevealed: false,
                x: 80,
                y: 80,
                width: 1000,
                fontSize: first.fontSize || 26,
                fontFamily: '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif'
            };
            this.currentPage.question = qObj;
        } else {
            // mode === 'new-pages'
            let firstAssigned = false;

            // If current page is empty (single page, no strokes, no question), use it for Q1
            if (this.pages.length === 1 && this.pages[0].strokes.length === 0 && !this.pages[0].question) {
                const q1 = selectedQuestions[0];
                this.pages[0].question = {
                    id: this.generateGuid(),
                    questionNumber: q1.questionNumber || 'Question 1',
                    questionText: q1.questionText || '',
                    options: (q1.options || []).map((opt, i) => ({
                        label: opt.label || String.fromCharCode(65 + i),
                        text: opt.text || ''
                    })),
                    correctAnswer: q1.correctAnswer || '',
                    isAnswerRevealed: false,
                    x: 80,
                    y: 80,
                    width: 1000,
                    fontSize: q1.fontSize || 26,
                    fontFamily: '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif'
                };
                firstAssigned = true;
            }

            const itemsToAdd = firstAssigned ? selectedQuestions.slice(1) : selectedQuestions;

            for (const q of itemsToAdd) {
                const newIdx = this.pages.length;
                this.pages.push({
                    index: newIdx,
                    title: `Page ${newIdx + 1}`,
                    background: this.activeBackground,
                    question: {
                        id: this.generateGuid(),
                        questionNumber: q.questionNumber || `Question ${newIdx + 1}`,
                        questionText: q.questionText || '',
                        options: (q.options || []).map((opt, i) => ({
                            label: opt.label || String.fromCharCode(65 + i),
                            text: opt.text || ''
                        })),
                        correctAnswer: q.correctAnswer || '',
                        isAnswerRevealed: false,
                        x: 80,
                        y: 80,
                        width: 1000,
                        fontSize: q.fontSize || 26,
                        fontFamily: '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif'
                    },
                    strokes: []
                });
            }

            if (!firstAssigned && itemsToAdd.length > 0) {
                this.setPageIndex(prevPages.length);
            }
        }

        // Push to undo stack
        this.undoStack.push({
            type: 'importQuestions',
            prevPages: prevPages,
            prevPageIndex: prevPageIndex,
            nextPages: JSON.parse(JSON.stringify(this.pages)),
            nextPageIndex: this.currentPageIndex
        });
        this.redoStack = [];

        document.getElementById('pageIndicator').textContent = `Page ${this.currentPageIndex + 1} of ${this.pages.length}`;
        this.renderCanvas();
        this.syncQuestionOverlay();
    }

    // ==========================================
    // Presenter Reference Materials (Multi-File & Any Type)
    // ==========================================
    bindSlideEvents() {
        const fileInput = document.getElementById('slideFileInput');
        const btnLoad = document.getElementById('btnLoadSlides');
        const btnClearAll = document.getElementById('btnClearAllSlides');
        const btnRemoveCurrent = document.getElementById('btnRemoveCurrentSlide');
        const sidebarLeft = document.getElementById('sidebarLeft');
        const viewport = document.getElementById('slideViewport');

        btnLoad.addEventListener('click', () => fileInput.click());

        fileInput.addEventListener('change', async (e) => {
            const files = Array.from(e.target.files);
            if (files.length === 0) return;
            await this.addReferenceFiles(files);
            fileInput.value = ''; // Reset input to allow re-uploading same file name if needed
        });

        if (btnClearAll) {
            btnClearAll.addEventListener('click', () => {
                if (confirm('Remove all reference materials?')) {
                    this.clearAllSlides();
                }
            });
        }

        if (btnRemoveCurrent) {
            btnRemoveCurrent.addEventListener('click', () => {
                if (this.currentSlideIndex >= 0 && this.slides.length > 0) {
                    this.removeSlide(this.currentSlideIndex);
                }
            });
        }

        document.getElementById('btnPrevSlide').addEventListener('click', () => {
            if (this.currentSlideIndex > 0) {
                this.currentSlideIndex--;
                this.updateSlideView();
            }
        });

        document.getElementById('btnNextSlide').addEventListener('click', () => {
            if (this.currentSlideIndex < this.slides.length - 1) {
                this.currentSlideIndex++;
                this.updateSlideView();
            }
        });

        // Drag & Drop Reference Material Upload
        const handleDragOver = (e) => {
            e.preventDefault();
            e.stopPropagation();
            viewport.classList.add('drag-over');
        };

        const handleDragLeave = (e) => {
            e.preventDefault();
            e.stopPropagation();
            viewport.classList.remove('drag-over');
        };

        const handleDrop = async (e) => {
            e.preventDefault();
            e.stopPropagation();
            viewport.classList.remove('drag-over');
            if (e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files.length > 0) {
                await this.addReferenceFiles(Array.from(e.dataTransfer.files));
            }
        };

        viewport.addEventListener('dragover', handleDragOver);
        viewport.addEventListener('dragleave', handleDragLeave);
        viewport.addEventListener('drop', handleDrop);
    }

    async addReferenceFiles(files) {
        for (const file of files) {
            const ext = file.name.split('.').pop().toLowerCase();
            let fileType = 'other';
            let contentUrl = null;
            let textContent = null;

            if (file.type.startsWith('image/') || ['png', 'jpg', 'jpeg', 'gif', 'svg', 'webp', 'bmp'].includes(ext)) {
                fileType = 'image';
                contentUrl = URL.createObjectURL(file);
                this.slides.push({
                    id: this.generateGuid(),
                    name: file.name,
                    type: fileType,
                    url: contentUrl,
                    textContent: null,
                    rawFile: file,
                    sizeBytes: file.size
                });
            } else if (file.type === 'application/pdf' || ext === 'pdf') {
                fileType = 'pdf';
                contentUrl = URL.createObjectURL(file);
                this.slides.push({
                    id: this.generateGuid(),
                    name: file.name,
                    type: fileType,
                    url: contentUrl,
                    textContent: null,
                    rawFile: file,
                    sizeBytes: file.size
                });
            } else if (['pptx', 'ppsx', 'pptm', 'potx', 'odp'].includes(ext)) {
                // PowerPoint Presentation (OpenXML / JSZip)
                await this.loadPptxPresentation(file);
            } else if (ext === 'ppt') {
                // Legacy PowerPoint Binary
                await this.loadPptBinaryPresentation(file);
            } else if (file.type.startsWith('text/') || ['txt', 'md', 'cs', 'js', 'ts', 'py', 'json', 'html', 'css', 'cpp', 'c', 'h', 'java', 'sql', 'sh', 'xml', 'yaml', 'yml', 'rs', 'go'].includes(ext)) {
                fileType = 'text';
                textContent = await new Promise((resolve) => {
                    const reader = new FileReader();
                    reader.onload = () => resolve(reader.result);
                    reader.onerror = () => resolve('Error reading file content');
                    reader.readAsText(file);
                });
                this.slides.push({
                    id: this.generateGuid(),
                    name: file.name,
                    type: fileType,
                    url: null,
                    textContent: textContent,
                    rawFile: file,
                    sizeBytes: file.size
                });
            } else {
                fileType = 'other';
                contentUrl = URL.createObjectURL(file);
                this.slides.push({
                    id: this.generateGuid(),
                    name: file.name,
                    type: fileType,
                    url: contentUrl,
                    textContent: null,
                    rawFile: file,
                    sizeBytes: file.size
                });
            }
        }

        if (this.currentSlideIndex < 0 || this.currentSlideIndex >= this.slides.length) {
            this.currentSlideIndex = this.slides.length - 1;
        }

        this.updateSlideView();
    }

    // ==========================================
    // 📊 PowerPoint (.pptx & .ppt) Presentation Parser
    // ==========================================
    async loadPptxPresentation(file) {
        try {
            if (typeof JSZip === 'undefined') {
                console.warn('JSZip not yet available, waiting...');
                await new Promise(r => setTimeout(r, 200));
            }

            const zip = await JSZip.loadAsync(await file.arrayBuffer());
            const parser = new DOMParser();

            // Find all slides in order
            const slideFiles = Object.keys(zip.files)
                .filter(name => /^ppt\/slides\/slide\d+\.xml$/i.test(name))
                .sort((a, b) => {
                    const numA = parseInt(a.match(/\d+/)[0], 10);
                    const numB = parseInt(b.match(/\d+/)[0], 10);
                    return numA - numB;
                });

            if (slideFiles.length === 0) {
                this.slides.push({
                    id: this.generateGuid(),
                    name: file.name,
                    type: 'pptx',
                    presentationName: file.name,
                    slideNumber: 1,
                    totalSlidesInDeck: 1,
                    title: file.name,
                    bullets: [{ text: 'Presentation loaded (No slide XML found)', level: 0 }],
                    tables: [],
                    images: [],
                    speakerNotes: '',
                    sizeBytes: file.size
                });
                return;
            }

            const totalSlides = slideFiles.length;

            for (let i = 0; i < totalSlides; i++) {
                const slideFileName = slideFiles[i];
                const slideNum = i + 1;
                const slideXmlStr = await zip.file(slideFileName).async('string');
                const slideDoc = parser.parseFromString(slideXmlStr, 'application/xml');

                // Extract Slide Images from relationships
                const relsFileName = `ppt/slides/_rels/${slideFileName.split('/').pop()}.rels`;
                const slideImages = [];
                if (zip.file(relsFileName)) {
                    const relsXml = await zip.file(relsFileName).async('string');
                    const relsDoc = parser.parseFromString(relsXml, 'application/xml');
                    const relNodes = relsDoc.getElementsByTagName('Relationship');
                    for (let r = 0; r < relNodes.length; r++) {
                        const target = relNodes[r].getAttribute('Target') || '';
                        if (target.includes('media/')) {
                            const mediaPath = 'ppt/' + target.replace('../', '');
                            const imgFile = zip.file(mediaPath);
                            if (imgFile) {
                                const blob = await imgFile.async('blob');
                                const imgUrl = URL.createObjectURL(blob);
                                slideImages.push({ url: imgUrl, name: target.split('/').pop() });
                            }
                        }
                    }
                }

                // Extract Speaker Notes if present
                let speakerNotes = '';
                let notesFilePath = `ppt/notesSlides/notesSlide${slideNum}.xml`;

                if (zip.file(relsFileName)) {
                    try {
                        const relsXml = await zip.file(relsFileName).async('string');
                        const relsDoc = parser.parseFromString(relsXml, 'application/xml');
                        const relNodes = relsDoc.getElementsByTagName('Relationship');
                        for (let r = 0; r < relNodes.length; r++) {
                            const relType = relNodes[r].getAttribute('Type') || '';
                            const target = relNodes[r].getAttribute('Target') || '';
                            if (relType.includes('notesSlide') || target.includes('notesSlide')) {
                                notesFilePath = 'ppt/' + target.replace(/^\.\.\//, '');
                                break;
                            }
                        }
                    } catch {}
                }

                const notesFile = zip.file(notesFilePath);
                if (notesFile) {
                    try {
                        const notesXml = await notesFile.async('string');
                        const notesDoc = parser.parseFromString(notesXml, 'application/xml');
                        const shapes = notesDoc.getElementsByTagName('p:sp');
                        const noteParagraphs = [];

                        for (let s = 0; s < shapes.length; s++) {
                            const shape = shapes[s];
                            const ph = shape.getElementsByTagName('p:ph')[0];
                            const phType = ph ? ph.getAttribute('type') : null;

                            // Skip slide thumbnail image placeholder
                            if (phType === 'sldImg') continue;

                            const paragraphs = shape.getElementsByTagName('a:p');
                            for (let p = 0; p < paragraphs.length; p++) {
                                const para = paragraphs[p];
                                const textNodes = para.getElementsByTagName('a:t');
                                let pText = '';
                                for (let t = 0; t < textNodes.length; t++) {
                                    pText += textNodes[t].textContent;
                                }
                                pText = pText.trim();
                                if (pText && !/^\d+$/.test(pText)) {
                                    noteParagraphs.push(pText);
                                }
                            }
                        }

                        if (noteParagraphs.length === 0) {
                            const textNodes = notesDoc.getElementsByTagName('a:t');
                            for (let t = 0; t < textNodes.length; t++) {
                                const txt = textNodes[t].textContent.trim();
                                if (txt && !/^\d+$/.test(txt)) noteParagraphs.push(txt);
                            }
                        }

                        speakerNotes = noteParagraphs.join('\n\n');
                    } catch (err) {
                        console.warn('Could not parse notes slide:', err);
                    }
                }

                // Extract Slide Title
                let slideTitle = '';
                const shapes = slideDoc.getElementsByTagName('p:sp');
                const bullets = [];
                const tables = [];

                for (let s = 0; s < shapes.length; s++) {
                    const shape = shapes[s];
                    const ph = shape.getElementsByTagName('p:ph')[0];
                    const phType = ph ? ph.getAttribute('type') : null;
                    const paragraphs = shape.getElementsByTagName('a:p');

                    for (let p = 0; p < paragraphs.length; p++) {
                        const para = paragraphs[p];
                        const textRuns = para.getElementsByTagName('a:t');
                        let paraText = '';
                        for (let t = 0; t < textRuns.length; t++) {
                            paraText += textRuns[t].textContent;
                        }
                        paraText = paraText.trim();
                        if (!paraText) continue;

                        const pPr = para.getElementsByTagName('a:pPr')[0];
                        const lvl = pPr && pPr.getAttribute('lvl') ? parseInt(pPr.getAttribute('lvl'), 10) : 0;

                        if ((phType === 'title' || phType === 'ctrTitle') && !slideTitle) {
                            slideTitle = paraText;
                        } else if (s === 0 && p === 0 && !slideTitle && !ph) {
                            slideTitle = paraText;
                        } else {
                            bullets.push({ text: paraText, level: lvl });
                        }
                    }
                }

                // Extract Tables
                const tblNodes = slideDoc.getElementsByTagName('a:tbl');
                for (let t = 0; t < tblNodes.length; t++) {
                    const tbl = tblNodes[t];
                    const rows = tbl.getElementsByTagName('a:tr');
                    const tableData = [];
                    for (let r = 0; r < rows.length; r++) {
                        const cells = rows[r].getElementsByTagName('a:tc');
                        const rowData = [];
                        for (let c = 0; c < cells.length; c++) {
                            const cellTexts = cells[c].getElementsByTagName('a:t');
                            let cText = '';
                            for (let ct = 0; ct < cellTexts.length; ct++) cText += cellTexts[ct].textContent;
                            rowData.push(cText.trim());
                        }
                        tableData.push(rowData);
                    }
                    if (tableData.length > 0) tables.push(tableData);
                }

                this.slides.push({
                    id: this.generateGuid(),
                    name: `📊 Slide ${slideNum}/${totalSlides}${slideTitle ? ': ' + slideTitle : ''}`,
                    type: 'pptx',
                    presentationName: file.name,
                    slideNumber: slideNum,
                    totalSlidesInDeck: totalSlides,
                    title: slideTitle || `Slide ${slideNum}`,
                    bullets: bullets,
                    tables: tables,
                    images: slideImages,
                    speakerNotes: speakerNotes,
                    sizeBytes: file.size
                });
            }
        } catch (err) {
            console.error('Failed to parse PPTX:', err);
            this.slides.push({
                id: this.generateGuid(),
                name: file.name,
                type: 'pptx',
                presentationName: file.name,
                slideNumber: 1,
                totalSlidesInDeck: 1,
                title: file.name,
                bullets: [{ text: `Error reading PPTX: ${err.message}. You can also export slides to PDF or images for visual preview.`, level: 0 }],
                tables: [],
                images: [],
                speakerNotes: '',
                sizeBytes: file.size
            });
        }
    }

    async loadPptBinaryPresentation(file) {
        try {
            const buffer = await file.arrayBuffer();
            const bytes = new Uint8Array(buffer);
            const extractedImages = [];
            const imageRanges = [];

            // 1. Locate and extract embedded PNG images (Magic: 89 50 4E 47)
            for (let i = 0; i < bytes.length - 8; i++) {
                if (bytes[i] === 0x89 && bytes[i+1] === 0x50 && bytes[i+2] === 0x4E && bytes[i+3] === 0x47 &&
                    bytes[i+4] === 0x0D && bytes[i+5] === 0x0A && bytes[i+6] === 0x1A && bytes[i+7] === 0x0A) {
                    let endIdx = -1;
                    for (let j = i + 8; j < bytes.length - 7; j++) {
                        if (bytes[j] === 0x49 && bytes[j+1] === 0x45 && bytes[j+2] === 0x4E && bytes[j+3] === 0x44 &&
                            bytes[j+4] === 0xAE && bytes[j+5] === 0x42 && bytes[j+6] === 0x60 && bytes[j+7] === 0x82) {
                            endIdx = j + 8;
                            break;
                        }
                    }
                    if (endIdx > i && (endIdx - i) < 25000000) {
                        const pngBytes = bytes.slice(i, endIdx);
                        const blob = new Blob([pngBytes], { type: 'image/png' });
                        extractedImages.push({
                            url: URL.createObjectURL(blob),
                            name: `diagram_${extractedImages.length + 1}.png`
                        });
                        imageRanges.push({ start: i, end: endIdx });
                        i = endIdx;
                    }
                }
            }

            // 2. Locate and extract embedded JPEG images (Magic: FF D8 FF)
            for (let i = 0; i < bytes.length - 3; i++) {
                if (bytes[i] === 0xFF && bytes[i+1] === 0xD8 && bytes[i+2] === 0xFF) {
                    let endIdx = -1;
                    for (let j = i + 3; j < bytes.length - 1; j++) {
                        if (bytes[j] === 0xFF && bytes[j+1] === 0xD9) {
                            endIdx = j + 2;
                            break;
                        }
                    }
                    if (endIdx > i && (endIdx - i) < 25000000) {
                        const jpegBytes = bytes.slice(i, endIdx);
                        const blob = new Blob([jpegBytes], { type: 'image/jpeg' });
                        extractedImages.push({
                            url: URL.createObjectURL(blob),
                            name: `slide_image_${extractedImages.length + 1}.jpg`
                        });
                        imageRanges.push({ start: i, end: endIdx });
                        i = endIdx;
                    }
                }
            }

            const isInImageRange = (pos) => {
                for (let r = 0; r < imageRanges.length; r++) {
                    if (pos >= imageRanges[r].start && pos < imageRanges[r].end) return true;
                }
                return false;
            };

            const isHumanText = (str) => {
                if (!str || str.length < 3 || str.length > 250) return false;
                // Strict rejection of binary image chunks / noise
                if (/PNG|IHDR|IDAT|IEND|JFIF|Exif|Adobe|sRGB|gAMA|cHRM|Photoshop|Current User|PowerPoint Document|SummaryInformation|DocumentSummaryInformation|Default Design|Arial|Calibri|Times New Roman|Wingdings|Tahoma/i.test(str)) {
                    return false;
                }
                const validCount = (str.match(/[a-zA-Z0-9\s.,!?:;"'()/\-_@#$%&*]/g) || []).length;
                if (validCount / str.length < 0.75) return false;
                if (!/[a-zA-Z]{2,}/.test(str)) return false;
                return true;
            };

            // 3. Extract UTF-16LE text strings outside image ranges
            const textChunks = [];
            let cur16 = '';
            for (let i = 0; i < bytes.length - 1; i += 2) {
                if (isInImageRange(i)) continue;
                const code = bytes[i] | (bytes[i + 1] << 8);
                if ((code >= 32 && code <= 126) || code === 10 || code === 13 || (code >= 160 && code <= 65533)) {
                    cur16 += String.fromCharCode(code);
                } else {
                    const trimmed = cur16.trim();
                    if (isHumanText(trimmed)) textChunks.push(trimmed);
                    cur16 = '';
                }
            }
            if (isHumanText(cur16.trim())) textChunks.push(cur16.trim());

            // 4. Extract ASCII text strings outside image ranges
            let curAscii = '';
            for (let i = 0; i < bytes.length; i++) {
                if (isInImageRange(i)) continue;
                const b = bytes[i];
                if ((b >= 32 && b <= 126) || b === 10 || b === 13) {
                    curAscii += String.fromCharCode(b);
                } else {
                    const trimmed = curAscii.trim();
                    if (isHumanText(trimmed)) textChunks.push(trimmed);
                    curAscii = '';
                }
            }
            if (isHumanText(curAscii.trim())) textChunks.push(curAscii.trim());

            // Deduplicate strings
            const uniqueChunks = [];
            const seen = new Set();
            for (const chunk of textChunks) {
                if (!seen.has(chunk.toLowerCase())) {
                    seen.add(chunk.toLowerCase());
                    uniqueChunks.push(chunk);
                }
            }

            if (uniqueChunks.length === 0 && extractedImages.length === 0) {
                uniqueChunks.push(`PowerPoint Presentation: ${file.name}`);
                uniqueChunks.push('Presentation loaded. (Tip: For full vector graphics, save as .pptx or export to PDF).');
            }

            // Group into slides
            const itemsPerSlide = Math.max(3, Math.min(6, Math.ceil(uniqueChunks.length / 4)));
            const slideGroups = [];
            for (let i = 0; i < uniqueChunks.length; i += itemsPerSlide) {
                slideGroups.push(uniqueChunks.slice(i, i + itemsPerSlide));
            }

            if (slideGroups.length === 0) slideGroups.push([file.name]);

            const totalSlides = slideGroups.length;
            slideGroups.forEach((group, idx) => {
                const slideNum = idx + 1;
                const title = group[0].length < 60 ? group[0] : `Slide ${slideNum}`;
                const bullets = (group[0] === title ? group.slice(1) : group).map(t => ({ text: t, level: 0 }));

                // Attach extracted diagrams to slides
                const slideImages = [];
                if (extractedImages.length > 0) {
                    if (idx < extractedImages.length) {
                        slideImages.push(extractedImages[idx]);
                    } else if (idx === 0) {
                        slideImages.push(...extractedImages);
                    }
                }

                this.slides.push({
                    id: this.generateGuid(),
                    name: `📊 Slide ${slideNum}/${totalSlides}: ${title.slice(0, 26)}`,
                    type: 'pptx',
                    presentationName: file.name,
                    slideNumber: slideNum,
                    totalSlidesInDeck: totalSlides,
                    title: title,
                    bullets: bullets.length > 0 ? bullets : [{ text: 'Slide content', level: 0 }],
                    tables: [],
                    images: slideImages,
                    speakerNotes: 'Legacy .ppt binary presentation parsed.',
                    sizeBytes: file.size
                });
            });
        } catch (err) {
            console.error('Failed to parse PPT:', err);
            this.slides.push({
                id: this.generateGuid(),
                name: `📊 ${file.name}`,
                type: 'pptx',
                presentationName: file.name,
                slideNumber: 1,
                totalSlidesInDeck: 1,
                title: file.name,
                bullets: [{ text: `Error reading PPT: ${err.message}. You can export slides to PDF or images.`, level: 0 }],
                tables: [],
                images: [],
                speakerNotes: '',
                sizeBytes: file.size
            });
        }
    }

    removeSlide(index) {
        if (index >= 0 && index < this.slides.length) {
            const item = this.slides[index];
            if (item.url) URL.revokeObjectURL(item.url);
            if (item.images && Array.isArray(item.images)) {
                item.images.forEach(img => { if (img.url) URL.revokeObjectURL(img.url); });
            }
            this.slides.splice(index, 1);
            if (this.currentSlideIndex >= this.slides.length) {
                this.currentSlideIndex = this.slides.length - 1;
            }
            this.updateSlideView();
        }
    }

    clearAllSlides() {
        this.slides.forEach(s => {
            if (s.url) URL.revokeObjectURL(s.url);
            if (s.images && Array.isArray(s.images)) {
                s.images.forEach(img => { if (img.url) URL.revokeObjectURL(img.url); });
            }
        });
        this.slides = [];
        this.currentSlideIndex = -1;
        this.updateSlideView();
    }

    renderSlideTabs() {
        const tabsBar = document.getElementById('slideTabsBar');
        if (!tabsBar) return;

        if (this.slides.length <= 1) {
            tabsBar.style.display = 'none';
            tabsBar.innerHTML = '';
            return;
        }

        tabsBar.style.display = 'flex';
        tabsBar.innerHTML = '';

        this.slides.forEach((item, idx) => {
            const pill = document.createElement('div');
            pill.className = `slide-tab-pill ${idx === this.currentSlideIndex ? 'active' : ''}`;
            
            let icon = '📁';
            if (item.type === 'image') icon = '🖼';
            else if (item.type === 'pdf') icon = '📄';
            else if (item.type === 'pptx') icon = '📊';
            else if (item.type === 'text') icon = '📝';

            pill.innerHTML = `
                <span>${icon} ${item.name}</span>
                <span class="slide-tab-remove" title="Remove file">&times;</span>
            `;

            pill.addEventListener('click', (e) => {
                if (e.target.classList.contains('slide-tab-remove')) {
                    e.stopPropagation();
                    this.removeSlide(idx);
                } else {
                    this.currentSlideIndex = idx;
                    this.updateSlideView();
                }
            });

            tabsBar.appendChild(pill);
        });
    }

    updateSlideView() {
        const img = document.getElementById('currentSlideImg');
        const pdfFrame = document.getElementById('currentSlidePdf');
        const textPre = document.getElementById('currentSlideText');
        const pptxContainer = document.getElementById('currentSlidePptx');
        const placeholder = document.getElementById('slidePlaceholder');
        const navBar = document.getElementById('slideNavBar');
        const counter = document.getElementById('slideCounter');
        const fileName = document.getElementById('slideFileName');
        const btnClear = document.getElementById('btnClearAllSlides');

        this.renderSlideTabs();

        if (this.slides.length > 0 && this.currentSlideIndex >= 0) {
            const item = this.slides[this.currentSlideIndex];

            placeholder.style.display = 'none';
            navBar.style.display = 'flex';
            if (btnClear) btnClear.style.display = 'inline-flex';

            counter.textContent = `${this.currentSlideIndex + 1} / ${this.slides.length}`;
            if (fileName) fileName.textContent = item.presentationName || item.name;

            // Reset all view elements
            img.style.display = 'none';
            pdfFrame.style.display = 'none';
            textPre.style.display = 'none';
            if (pptxContainer) pptxContainer.style.display = 'none';

            if (item.type === 'image') {
                img.src = item.url;
                img.style.display = 'block';
            } else if (item.type === 'pdf') {
                pdfFrame.src = item.url;
                pdfFrame.style.display = 'block';
            } else if (item.type === 'text') {
                textPre.textContent = item.textContent || '';
                textPre.style.display = 'block';
            } else if (item.type === 'pptx') {
                if (pptxContainer) {
                    pptxContainer.innerHTML = '';

                    // Header Controls Bar
                    const header = document.createElement('div');
                    header.className = 'pptx-slide-header';
                    
                    const deckBadge = document.createElement('div');
                    deckBadge.className = 'pptx-deck-badge';
                    deckBadge.title = item.presentationName || 'PowerPoint Presentation';
                    deckBadge.innerHTML = `📊 ${item.presentationName || 'PowerPoint'}`;
                    header.appendChild(deckBadge);

                    const controls = document.createElement('div');
                    controls.className = 'pptx-viewer-controls';

                    // Jump Selector
                    const sameDeckSlides = this.slides
                        .map((s, i) => ({ slide: s, index: i }))
                        .filter(s => s.slide.presentationName === item.presentationName);

                    if (sameDeckSlides.length > 1) {
                        const selectJump = document.createElement('select');
                        selectJump.className = 'pptx-jump-select';
                        selectJump.title = 'Jump to slide';
                        sameDeckSlides.forEach(s => {
                            const opt = document.createElement('option');
                            opt.value = s.index;
                            opt.selected = s.index === this.currentSlideIndex;
                            opt.textContent = `Slide ${s.slide.slideNumber}: ${(s.slide.title || '').slice(0, 24)}`;
                            selectJump.appendChild(opt);
                        });
                        selectJump.addEventListener('change', (e) => {
                            this.currentSlideIndex = parseInt(e.target.value, 10);
                            this.updateSlideView();
                        });
                        controls.appendChild(selectJump);
                    }

                    // Font Size Buttons
                    const btnFontDec = document.createElement('button');
                    btnFontDec.className = 'pptx-ctrl-btn';
                    btnFontDec.textContent = 'A-';
                    btnFontDec.title = 'Decrease text size';
                    btnFontDec.addEventListener('click', () => {
                        this.slideFontSize = Math.max(12, (this.slideFontSize || 16) - 2);
                        this.updateSlideView();
                    });
                    controls.appendChild(btnFontDec);

                    const btnFontInc = document.createElement('button');
                    btnFontInc.className = 'pptx-ctrl-btn';
                    btnFontInc.textContent = 'A+';
                    btnFontInc.title = 'Increase text size';
                    btnFontInc.addEventListener('click', () => {
                        this.slideFontSize = Math.min(26, (this.slideFontSize || 16) + 2);
                        this.updateSlideView();
                    });
                    controls.appendChild(btnFontInc);

                    // Theme Toggle
                    const btnTheme = document.createElement('button');
                    btnTheme.className = 'pptx-ctrl-btn';
                    btnTheme.innerHTML = this.slideTheme === 'dark' ? '☀️ Light' : '🌙 Dark';
                    btnTheme.title = 'Toggle Slide Contrast Theme';
                    btnTheme.addEventListener('click', () => {
                        this.slideTheme = this.slideTheme === 'dark' ? 'light' : 'dark';
                        this.updateSlideView();
                    });
                    controls.appendChild(btnTheme);

                    // Fullscreen Expand
                    const btnExpand = document.createElement('button');
                    btnExpand.className = 'pptx-ctrl-btn';
                    btnExpand.innerHTML = '⛶ HD';
                    btnExpand.title = 'Open Fullscreen Slide View';
                    btnExpand.addEventListener('click', () => this.openSlideFullscreenModal(item));
                    controls.appendChild(btnExpand);

                    header.appendChild(controls);
                    pptxContainer.appendChild(header);

                    // Slide Card Canvas
                    const card = this.createPptxSlideCardElement(item);
                    pptxContainer.appendChild(card);

                    // Speaker Notes Box (Private)
                    if (item.speakerNotes && item.speakerNotes.trim().length > 0) {
                        const notesBox = document.createElement('div');
                        notesBox.className = 'pptx-notes-box';
                        notesBox.innerHTML = `
                            <div class="pptx-notes-header">
                                <div class="pptx-notes-label">🎙️ Presenter / Speaker Notes (Private)</div>
                            </div>
                            <div class="pptx-notes-content">${this.formatSpeakerNotesHtml(item.speakerNotes)}</div>
                        `;
                        pptxContainer.appendChild(notesBox);
                    }

                    pptxContainer.style.display = 'flex';
                }
            } else {
                if (pptxContainer) {
                    pptxContainer.innerHTML = '';
                    const card = document.createElement('div');
                    card.className = 'pptx-slide-card';
                    card.innerHTML = `
                        <div class="pptx-title">📁 ${item.name}</div>
                        <div style="font-size:12px; color:#94A3B8; margin-bottom:8px;">File size: ${(item.sizeBytes / 1024).toFixed(1)} KB</div>
                        <div style="display:flex; flex-wrap:wrap; gap:8px;">
                            <button id="btnParseAsPptx" class="btn btn-sm btn-primary">📊 Parse as PowerPoint / Presentation</button>
                            <button id="btnViewExtractedText" class="btn btn-sm btn-secondary">📝 Extract & View Text</button>
                            ${item.url ? `<a href="${item.url}" download="${item.name}" class="btn btn-sm btn-secondary">⬇ Download</a>` : ''}
                        </div>
                    `;
                    pptxContainer.appendChild(card);

                    const btnParse = card.querySelector('#btnParseAsPptx');
                    if (btnParse && item.rawFile) {
                        btnParse.addEventListener('click', async () => {
                            btnParse.textContent = '⏳ Parsing Slides...';
                            const file = item.rawFile;
                            const idx = this.currentSlideIndex;
                            const ext = file.name.split('.').pop().toLowerCase();
                            if (ext === 'ppt') await this.loadPptBinaryPresentation(file);
                            else await this.loadPptxPresentation(file);
                            this.removeSlide(idx);
                        });
                    }

                    const btnText = card.querySelector('#btnViewExtractedText');
                    if (btnText && item.rawFile) {
                        btnText.addEventListener('click', async () => {
                            btnText.textContent = '⏳ Extracting Text...';
                            const file = item.rawFile;
                            const idx = this.currentSlideIndex;
                            await this.loadPptBinaryPresentation(file);
                            this.removeSlide(idx);
                        });
                    }

                    pptxContainer.style.display = 'flex';
                } else {
                    textPre.textContent = `📁 File: ${item.name}\nSize: ${(item.sizeBytes / 1024).toFixed(1)} KB`;
                    textPre.style.display = 'block';
                }
            }
        } else {
            img.style.display = 'none';
            pdfFrame.style.display = 'none';
            textPre.style.display = 'none';
            if (pptxContainer) pptxContainer.style.display = 'none';
            placeholder.style.display = 'block';
            navBar.style.display = 'none';
            if (btnClear) btnClear.style.display = 'none';
        }
    }

    // ==========================================
    // 📱 Mobile Drawer & Split View Events
    // ==========================================
    bindMobileDrawerEvents() {
        const btnSplit = document.getElementById('btnMobileSplitView');
        const btnToggleTool = document.getElementById('btnToggleToolSidebar');
        const btnCloseRef = document.getElementById('btnCloseRefSidebar');
        const btnCloseTool = document.getElementById('btnCloseToolSidebar');
        const sidebarLeft = document.getElementById('sidebarLeft');
        const sidebarRight = document.getElementById('sidebarRight');
        const backdrop = document.getElementById('sidebarBackdrop');
        const studioMain = document.querySelector('.studio-main');

        const closeAllDrawers = () => {
            if (sidebarLeft) sidebarLeft.classList.remove('sidebar-drawer-open');
            if (sidebarRight) sidebarRight.classList.remove('sidebar-drawer-open');
            if (backdrop) backdrop.style.display = 'none';
        };

        if (btnSplit && studioMain) {
            btnSplit.addEventListener('click', () => {
                const isSplit = studioMain.classList.toggle('mobile-split-active');
                if (isSplit) {
                    btnSplit.textContent = '📑 Split (ON)';
                    btnSplit.classList.remove('btn-secondary');
                    btnSplit.classList.add('btn-primary');
                } else {
                    btnSplit.textContent = '📑 Split Notes';
                    btnSplit.classList.remove('btn-primary');
                    btnSplit.classList.add('btn-secondary');
                }
                closeAllDrawers();
                setTimeout(() => this.setupCanvasSize(), 50);
            });
        }

        if (btnToggleTool && sidebarRight) {
            btnToggleTool.addEventListener('click', () => {
                const isOpen = sidebarRight.classList.contains('sidebar-drawer-open');
                closeAllDrawers();
                if (!isOpen) {
                    sidebarRight.classList.add('sidebar-drawer-open');
                    if (backdrop) backdrop.style.display = 'block';
                }
            });
        }

        if (btnCloseRef) {
            btnCloseRef.addEventListener('click', () => {
                if (studioMain) studioMain.classList.remove('mobile-split-active');
                if (btnSplit) {
                    btnSplit.textContent = '📑 Split Notes';
                    btnSplit.classList.remove('btn-primary');
                    btnSplit.classList.add('btn-secondary');
                }
                closeAllDrawers();
                setTimeout(() => this.setupCanvasSize(), 50);
            });
        }

        if (btnCloseTool) btnCloseTool.addEventListener('click', closeAllDrawers);
        if (backdrop) backdrop.addEventListener('click', closeAllDrawers);
    }

    // ==========================================
    // ↔ Draggable Splitter Resizer (Desktop & Mobile)
    // ==========================================
    bindResizerEvents() {
        const resizer = document.getElementById('resizerLeft');
        const sidebar = document.getElementById('sidebarLeft');
        const studioMain = document.querySelector('.studio-main');
        if (!resizer || !sidebar || !studioMain) return;

        let isResizing = false;

        const onPointerDown = (e) => {
            isResizing = true;
            resizer.classList.add('resizing');
            document.body.style.cursor = window.innerWidth <= 992 && window.innerHeight > window.innerWidth ? 'row-resize' : 'col-resize';
            document.body.style.userSelect = 'none';
            resizer.setPointerCapture(e.pointerId);
        };

        const onPointerMove = (e) => {
            if (!isResizing) return;
            const isMobilePortrait = window.innerWidth <= 992 && window.innerHeight > window.innerWidth;
            const containerRect = studioMain.getBoundingClientRect();

            if (isMobilePortrait) {
                // Vertical touch resizing on portrait mobile
                let newHeight = e.clientY - containerRect.top;
                const minH = 110;
                const maxH = containerRect.height - 180;
                newHeight = Math.max(minH, Math.min(newHeight, maxH));
                sidebar.style.height = `${newHeight}px`;
                document.documentElement.style.setProperty('--mobile-ref-height', `${newHeight}px`);
            } else {
                // Horizontal resizing on desktop and landscape mobile
                let newWidth = e.clientX - containerRect.left;
                const minWidth = 180;
                const maxWidth = Math.max(260, containerRect.width - 260);
                newWidth = Math.max(minWidth, Math.min(newWidth, maxWidth));
                sidebar.style.width = `${newWidth}px`;
                document.documentElement.style.setProperty('--sidebar-left-width', `${newWidth}px`);
                document.documentElement.style.setProperty('--mobile-ref-width', `${newWidth}px`);
            }

            this.setupCanvasSize();
        };

        const onPointerUp = (e) => {
            if (isResizing) {
                isResizing = false;
                resizer.classList.remove('resizing');
                document.body.style.cursor = '';
                document.body.style.userSelect = '';
                try { resizer.releasePointerCapture(e.pointerId); } catch {}
                this.setupCanvasSize();
            }
        };

        resizer.addEventListener('pointerdown', onPointerDown);
        resizer.addEventListener('pointermove', onPointerMove);
        resizer.addEventListener('pointerup', onPointerUp);
        resizer.addEventListener('pointercancel', onPointerUp);

        // Double-click to toggle 50% split / default size
        resizer.addEventListener('dblclick', () => {
            const isMobilePortrait = window.innerWidth <= 992 && window.innerHeight > window.innerWidth;
            const containerRect = studioMain.getBoundingClientRect();

            if (isMobilePortrait) {
                const currentH = sidebar.getBoundingClientRect().height;
                let targetH = currentH < containerRect.height * 0.45 ? Math.round(containerRect.height * 0.5) : 180;
                sidebar.style.height = `${targetH}px`;
                document.documentElement.style.setProperty('--mobile-ref-height', `${targetH}px`);
            } else {
                const currentW = sidebar.getBoundingClientRect().width;
                let targetW = currentW < containerRect.width * 0.45 ? Math.round(containerRect.width * 0.5) : 340;
                sidebar.style.width = `${targetW}px`;
                document.documentElement.style.setProperty('--sidebar-left-width', `${targetW}px`);
            }
            this.setupCanvasSize();
        });
    }

    // ==========================================
    // Hardware Audio & Webcam PiP Controls
    // ==========================================
    bindMediaEvents() {
        const btnAudio = document.getElementById('btnEnableAudio');
        const btnCamera = document.getElementById('btnToggleCamera');
        const btnMobileMic = document.getElementById('btnMobileMic');
        const btnMobileCam = document.getElementById('btnMobileCam');
        const btnMobileMediaSettings = document.getElementById('btnMobileMediaSettings');
        const modalMediaSettings = document.getElementById('mediaSettingsModal');
        const btnCloseMediaSettings = document.getElementById('btnCloseMediaSettings');
        const btnCloseMediaSettingsFooter = document.getElementById('btnCloseMediaSettingsFooter');
        const mobileCameraLayoutSelector = document.getElementById('mobileCameraLayoutSelector');
        const chkMobileMirror = document.getElementById('chkMobileMirror');
        const video = document.getElementById('webcamVideo');
        const camPlaceholder = document.getElementById('camPlaceholder');
        const cameraPip = document.getElementById('cameraPip');
        const layoutSelector = document.getElementById('cameraLayoutSelector');
        const chkMirror = document.getElementById('chkMirror');

        const toggleAudioHandler = async () => {
            await this.enableMicrophone();
        };

        const toggleCameraHandler = async () => {
            if (this.cameraStream) {
                this.cameraStream.getTracks().forEach(t => t.stop());
                this.cameraStream = null;
                video.srcObject = null;
                camPlaceholder.style.display = 'flex';
                if (btnCamera) {
                    btnCamera.textContent = 'Enable Camera';
                    btnCamera.classList.remove('btn-primary');
                    btnCamera.classList.add('btn-secondary');
                }
                if (btnMobileCam) {
                    btnMobileCam.textContent = '📹 Cam';
                    btnMobileCam.classList.remove('btn-primary');
                    btnMobileCam.classList.add('btn-secondary');
                }
            } else {
                await this.enableCamera();
            }
        };

        if (btnAudio) btnAudio.addEventListener('click', toggleAudioHandler);
        if (btnMobileMic) btnMobileMic.addEventListener('click', toggleAudioHandler);

        if (btnCamera) btnCamera.addEventListener('click', toggleCameraHandler);
        if (btnMobileCam) btnMobileCam.addEventListener('click', toggleCameraHandler);

        if (btnMobileMediaSettings && modalMediaSettings) {
            btnMobileMediaSettings.addEventListener('click', () => {
                modalMediaSettings.style.display = 'block';
            });
            const closeSettings = () => { modalMediaSettings.style.display = 'none'; };
            if (btnCloseMediaSettings) btnCloseMediaSettings.addEventListener('click', closeSettings);
            if (btnCloseMediaSettingsFooter) btnCloseMediaSettingsFooter.addEventListener('click', closeSettings);
        }

        const applyLayoutPreset = (preset) => {
            cameraPip.className = `camera-pip pip-${preset.toLowerCase().replace('-', '')}`;
            if (preset === 'BottomRight') cameraPip.className = 'camera-pip pip-bottom-right';
            if (preset === 'BottomLeft') cameraPip.className = 'camera-pip pip-bottom-left';
            if (preset === 'TopRight') cameraPip.className = 'camera-pip pip-top-right';
            if (preset === 'TopLeft') cameraPip.className = 'camera-pip pip-top-left';
            if (preset === 'Fullscreen') cameraPip.className = 'camera-pip pip-fullscreen';
            if (preset === 'Hidden') cameraPip.className = 'camera-pip pip-hidden';

            this.cameraLayout.preset = preset;
            if (layoutSelector) layoutSelector.value = preset;
            if (mobileCameraLayoutSelector) mobileCameraLayoutSelector.value = preset;

            if (this.recordingState === 'Recording') {
                this.recordTimelineEvent({
                    $eventType: 'CameraLayoutChanged',
                    timestamp: this.getElapsedSessionTime(),
                    layout: { preset: preset, isMirrored: this.cameraLayout.isMirrored, isVisible: preset !== 'Hidden' }
                });
            }
            this.renderCanvas();
        };

        if (layoutSelector) {
            layoutSelector.addEventListener('change', (e) => applyLayoutPreset(e.target.value));
        }
        if (mobileCameraLayoutSelector) {
            mobileCameraLayoutSelector.addEventListener('change', (e) => applyLayoutPreset(e.target.value));
        }

        const applyMirror = (isMirrored) => {
            this.cameraLayout.isMirrored = isMirrored;
            if (chkMirror) chkMirror.checked = isMirrored;
            if (chkMobileMirror) chkMobileMirror.checked = isMirrored;
            cameraPip.classList.toggle('no-mirror', !isMirrored);
            this.renderCanvas();
        };

        if (chkMirror) {
            chkMirror.addEventListener('change', (e) => applyMirror(e.target.checked));
        }
        if (chkMobileMirror) {
            chkMobileMirror.addEventListener('change', (e) => applyMirror(e.target.checked));
        }
    }

    async enableMicrophone() {
        const btnAudio = document.getElementById('btnEnableAudio');
        const btnMobileMic = document.getElementById('btnMobileMic');
        const audioStatus = document.getElementById('audioStatus');

        try {
            this.audioStream = await navigator.mediaDevices.getUserMedia({ audio: true });
            this.audioContext = new (window.AudioContext || window.webkitAudioContext)();
            const source = this.audioContext.createMediaStreamSource(this.audioStream);
            this.analyserNode = this.audioContext.createAnalyser();
            this.analyserNode.fftSize = 256;
            source.connect(this.analyserNode);

            if (btnAudio) {
                btnAudio.textContent = '✓ Mic Connected';
                btnAudio.classList.remove('btn-secondary');
                btnAudio.classList.add('btn-primary');
            }
            if (btnMobileMic) {
                btnMobileMic.textContent = '✓ Mic';
                btnMobileMic.classList.remove('btn-secondary');
                btnMobileMic.classList.add('btn-primary');
            }
            if (audioStatus) {
                audioStatus.textContent = 'Microphone active — Live VU metering enabled';
            }

            this.startVuMeterLoop();
            return true;
        } catch (err) {
            if (audioStatus) audioStatus.textContent = `Microphone error: ${err.message}`;
            return false;
        }
    }

    async enableCamera() {
        const btnCamera = document.getElementById('btnToggleCamera');
        const btnMobileCam = document.getElementById('btnMobileCam');
        const video = document.getElementById('webcamVideo');
        const camPlaceholder = document.getElementById('camPlaceholder');

        try {
            this.cameraStream = await navigator.mediaDevices.getUserMedia({
                video: { width: { ideal: 1280 }, height: { ideal: 720 } }
            });
            video.srcObject = this.cameraStream;
            await video.play();
            camPlaceholder.style.display = 'none';
            if (btnCamera) {
                btnCamera.textContent = '✓ Camera Active';
                btnCamera.classList.remove('btn-secondary');
                btnCamera.classList.add('btn-primary');
            }
            if (btnMobileCam) {
                btnMobileCam.textContent = '✓ Cam';
                btnMobileCam.classList.remove('btn-secondary');
                btnMobileCam.classList.add('btn-primary');
            }
            this.renderCanvas();
            return true;
        } catch (err) {
            alert(`Camera access error: ${err.message}`);
            return false;
        }
    }

    startVuMeterLoop() {
        const vuBar = document.getElementById('vuMeterBar');
        const mobileVuBar = document.getElementById('mobileVuMeterBar');
        const dataArray = new Uint8Array(this.analyserNode.frequencyBinCount);

        const updateMeter = () => {
            if (this.analyserNode) {
                this.analyserNode.getByteFrequencyData(dataArray);
                let sum = 0;
                for (let i = 0; i < dataArray.length; i++) sum += dataArray[i];
                const avg = sum / dataArray.length;
                const percent = Math.min(100, Math.round((avg / 128) * 100));
                if (vuBar) vuBar.style.width = `${percent}%`;
                if (mobileVuBar) mobileVuBar.style.width = `${percent}%`;
            }
            requestAnimationFrame(updateMeter);
        };
        updateMeter();
    }

    getBestMimeType() {
        const types = [
            'video/mp4;codecs=avc1.42E01E,mp4a.40.2',
            'video/mp4;codecs=avc1',
            'video/mp4',
            'video/webm;codecs=h264',
            'video/webm;codecs=vp9,opus',
            'video/webm;codecs=vp8,opus',
            'video/webm'
        ];
        for (const t of types) {
            if (MediaRecorder.isTypeSupported && MediaRecorder.isTypeSupported(t)) {
                return t;
            }
        }
        return '';
    }

    // ==========================================
    // Synchronized Recording Engine & Local Storage
    // ==========================================
    bindRecordingEvents() {
        const btnRecord = document.getElementById('btnRecord');
        const btnPause = document.getElementById('btnPause');
        const btnResume = document.getElementById('btnResume');
        const btnStop = document.getElementById('btnStop');

        const btnMobileRecord = document.getElementById('btnMobileRecord');
        const btnMobilePause = document.getElementById('btnMobilePause');
        const btnMobileResume = document.getElementById('btnMobileResume');
        const btnMobileStop = document.getElementById('btnMobileStop');
        const btnMobileExport = document.getElementById('btnMobileExport');

        if (btnRecord) btnRecord.addEventListener('click', () => this.startRecording());
        if (btnPause) btnPause.addEventListener('click', () => this.pauseRecording());
        if (btnResume) btnResume.addEventListener('click', () => this.resumeRecording());
        if (btnStop) btnStop.addEventListener('click', () => this.stopRecording());

        if (btnMobileRecord) btnMobileRecord.addEventListener('click', () => this.startRecording());
        if (btnMobilePause) btnMobilePause.addEventListener('click', () => this.pauseRecording());
        if (btnMobileResume) btnMobileResume.addEventListener('click', () => this.resumeRecording());
        if (btnMobileStop) btnMobileStop.addEventListener('click', () => this.stopRecording());
        if (btnMobileExport) btnMobileExport.addEventListener('click', () => {
            document.getElementById('exportModal').style.display = 'block';
        });
    }

    async startRecording() {
        if (!this.audioStream) {
            await this.enableMicrophone();
        }

        this.recordingState = 'Recording';
        this.sessionStartTime = Date.now();
        this.totalPausedDuration = 0;
        this.timelineEvents = [];

        // 1. Microphone Audio Stream Recorder
        this.audioChunks = [];
        if (this.audioStream) {
            try {
                this.audioRecorder = new MediaRecorder(this.audioStream);
                this.audioRecorder.ondataavailable = (e) => {
                    if (e.data && e.data.size > 0) this.audioChunks.push(e.data);
                };
                this.audioRecorder.start(250);
            } catch (err) {
                console.warn('Audio recorder start warning:', err);
            }
        }

        // 2. Camera Video Stream Recorder
        this.cameraChunks = [];
        if (this.cameraStream && this.cameraLayout.preset !== 'Hidden') {
            try {
                const camMime = this.getBestMimeType();
                this.cameraRecorder = new MediaRecorder(this.cameraStream, camMime ? { mimeType: camMime } : undefined);
                this.cameraRecorder.ondataavailable = (e) => {
                    if (e.data && e.data.size > 0) this.cameraChunks.push(e.data);
                };
                this.cameraRecorder.start(250);
            } catch (err) {
                console.warn('Camera recorder start warning:', err);
            }
        }

        // 3. Combined Canvas + Webcam + Mic Audio Stream Local Recorder (Direct Browser MP4 Video Generation)
        try {
            const canvasStream = this.canvas.captureStream(30);
            const combinedTracks = [...canvasStream.getVideoTracks()];
            if (this.audioStream && this.audioStream.getAudioTracks().length > 0) {
                combinedTracks.push(this.audioStream.getAudioTracks()[0]);
            }
            const localStream = new MediaStream(combinedTracks);
            this.canvasChunks = [];
            const bestMime = this.getBestMimeType();
            const recorderOpts = bestMime ? { mimeType: bestMime, videoBitsPerSecond: 4000000 } : undefined;
            
            this.canvasRecorder = new MediaRecorder(localStream, recorderOpts);
            this.canvasRecorder.ondataavailable = (e) => {
                if (e.data && e.data.size > 0) this.canvasChunks.push(e.data);
            };
            this.canvasRecorder.start(250);
        } catch (err) {
            console.warn('Local canvas recorder warning:', err);
        }

        this.updateRecordingUi();
        this.startTimer();

        this.recordTimelineEvent({
            $eventType: 'RecordingStateChanged',
            timestamp: '00:00:00',
            oldState: 'Stopped',
            newState: 'Recording'
        });

        this.recordTimelineEvent({
            $eventType: 'CameraLayoutChanged',
            timestamp: '00:00:00',
            layout: { preset: this.cameraLayout.preset, isMirrored: this.cameraLayout.isMirrored, isVisible: true }
        });
    }

    pauseRecording() {
        this.recordingState = 'Paused';
        this.pauseStartTime = Date.now();
        
        if (this.audioRecorder && this.audioRecorder.state === 'recording') this.audioRecorder.pause();
        if (this.cameraRecorder && this.cameraRecorder.state === 'recording') this.cameraRecorder.pause();
        if (this.canvasRecorder && this.canvasRecorder.state === 'recording') this.canvasRecorder.pause();

        this.updateRecordingUi();

        this.recordTimelineEvent({
            $eventType: 'RecordingStateChanged',
            timestamp: this.getElapsedSessionTime(),
            oldState: 'Recording',
            newState: 'Paused'
        });
    }

    resumeRecording() {
        if (this.pauseStartTime) {
            this.totalPausedDuration += Date.now() - this.pauseStartTime;
            this.pauseStartTime = null;
        }
        this.recordingState = 'Recording';

        if (this.audioRecorder && this.audioRecorder.state === 'paused') this.audioRecorder.resume();
        if (this.cameraRecorder && this.cameraRecorder.state === 'paused') this.cameraRecorder.resume();
        if (this.canvasRecorder && this.canvasRecorder.state === 'paused') this.canvasRecorder.resume();

        this.updateRecordingUi();

        this.recordTimelineEvent({
            $eventType: 'RecordingStateChanged',
            timestamp: this.getElapsedSessionTime(),
            oldState: 'Paused',
            newState: 'Recording'
        });
    }

    stopRecording() {
        const finalTime = this.getElapsedSessionTime();
        this.recordingState = 'Stopped';
        clearInterval(this.timerInterval);

        // Stop Audio Recorder
        if (this.audioRecorder && this.audioRecorder.state !== 'inactive') {
            this.audioRecorder.onstop = () => {
                this.recordedAudioBlob = new Blob(this.audioChunks, { type: 'audio/webm' });
            };
            this.audioRecorder.stop();
        }

        // Stop Camera Recorder
        if (this.cameraRecorder && this.cameraRecorder.state !== 'inactive') {
            this.cameraRecorder.onstop = () => {
                this.recordedCameraBlob = new Blob(this.cameraChunks, { type: 'video/webm' });
            };
            this.cameraRecorder.stop();
        }

        // Stop Local Canvas Recorder & Save immediately to IndexedDB Local Storage as MP4
        if (this.canvasRecorder && this.canvasRecorder.state !== 'inactive') {
            this.canvasRecorder.onstop = async () => {
                const mime = this.getBestMimeType() || 'video/mp4';
                this.localVideoBlob = new Blob(this.canvasChunks, { type: mime.includes('mp4') ? 'video/mp4' : 'video/webm' });
                
                // Create thumbnail snapshot
                const thumb = this.canvas.toDataURL('image/jpeg', 0.6);
                const recordingEntry = {
                    id: this.generateGuid(),
                    title: `Lesson ${new Date().toLocaleDateString()} ${new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`,
                    createdAt: new Date().toISOString(),
                    duration: finalTime.split('.')[0],
                    format: 'MP4 (Synchronized Studio Video)',
                    sizeBytes: this.localVideoBlob.size,
                    blob: this.localVideoBlob,
                    thumbnail: thumb,
                    pages: JSON.parse(JSON.stringify(this.pages)),
                    events: JSON.parse(JSON.stringify(this.timelineEvents))
                };

                await this.storage.saveRecording(recordingEntry);
                this.updateLibraryBadge();
            };
            this.canvasRecorder.stop();
        }

        this.recordTimelineEvent({
            $eventType: 'RecordingStateChanged',
            timestamp: finalTime,
            oldState: 'Recording',
            newState: 'Stopped'
        });

        this.updateRecordingUi();
        
        // Open export dialog with instant download ready
        const instantBox = document.getElementById('instantDownloadBox');
        if (instantBox) instantBox.style.display = 'block';
        document.getElementById('exportModal').style.display = 'flex';
    }

    getElapsedSessionTime() {
        if (!this.sessionStartTime) return '00:00:00.000';
        let now = Date.now();
        if (this.recordingState === 'Paused' && this.pauseStartTime) {
            now = this.pauseStartTime;
        }
        const totalMs = (now - this.sessionStartTime) - this.totalPausedDuration;
        const totalSec = Math.max(0, totalMs / 1000);
        return this.formatTimeSpan(totalSec);
    }

    formatTimeSpan(seconds) {
        const hrs = Math.floor(seconds / 3600);
        const mins = Math.floor((seconds % 3600) / 60);
        const secs = Math.floor(seconds % 60);
        const ms = Math.floor((seconds % 1) * 1000);
        return `${String(hrs).padStart(2, '0')}:${String(mins).padStart(2, '0')}:${String(secs).padStart(2, '0')}.${String(ms).padStart(3, '0')}`;
    }

    startTimer() {
        clearInterval(this.timerInterval);
        this.timerInterval = setInterval(() => {
            const timeStr = this.getElapsedSessionTime().split('.')[0];
            document.getElementById('timerDisplay').textContent = timeStr;
        }, 100);
    }

    updateRecordingUi() {
        const pill = document.getElementById('recordingPill');
        const statusLabel = document.getElementById('statusLabel');
        const btnRecord = document.getElementById('btnRecord');
        const btnPause = document.getElementById('btnPause');
        const btnResume = document.getElementById('btnResume');
        const btnStop = document.getElementById('btnStop');

        const btnMobileRecord = document.getElementById('btnMobileRecord');
        const btnMobilePause = document.getElementById('btnMobilePause');
        const btnMobileResume = document.getElementById('btnMobileResume');
        const btnMobileStop = document.getElementById('btnMobileStop');

        pill.className = `recording-pill state-${this.recordingState.toLowerCase()}`;
        statusLabel.textContent = this.recordingState.toUpperCase();

        if (this.recordingState === 'Recording') {
            if (btnRecord) btnRecord.style.display = 'none';
            if (btnPause) btnPause.style.display = 'inline-flex';
            if (btnResume) btnResume.style.display = 'none';
            if (btnStop) btnStop.style.display = 'inline-flex';

            if (btnMobileRecord) btnMobileRecord.style.display = 'none';
            if (btnMobilePause) btnMobilePause.style.display = 'inline-flex';
            if (btnMobileResume) btnMobileResume.style.display = 'none';
            if (btnMobileStop) btnMobileStop.style.display = 'inline-flex';
        } else if (this.recordingState === 'Paused') {
            if (btnRecord) btnRecord.style.display = 'none';
            if (btnPause) btnPause.style.display = 'none';
            if (btnResume) btnResume.style.display = 'inline-flex';
            if (btnStop) btnStop.style.display = 'inline-flex';

            if (btnMobileRecord) btnMobileRecord.style.display = 'none';
            if (btnMobilePause) btnMobilePause.style.display = 'none';
            if (btnMobileResume) btnMobileResume.style.display = 'inline-flex';
            if (btnMobileStop) btnMobileStop.style.display = 'inline-flex';
        } else {
            if (btnRecord) btnRecord.style.display = 'inline-flex';
            if (btnPause) btnPause.style.display = 'none';
            if (btnResume) btnResume.style.display = 'none';
            if (btnStop) btnStop.style.display = 'none';

            if (btnMobileRecord) btnMobileRecord.style.display = 'inline-flex';
            if (btnMobilePause) btnMobilePause.style.display = 'none';
            if (btnMobileResume) btnMobileResume.style.display = 'none';
            if (btnMobileStop) btnMobileStop.style.display = 'none';
        }
    }

    recordTimelineEvent(evt) {
        evt.eventId = this.generateGuid();
        evt.wallClockUtc = new Date().toISOString();
        this.timelineEvents.push(evt);
    }

    // ==========================================
    // Video Export & Instant MP4 Download
    // ==========================================
    bindExportEvents() {
        const modal = document.getElementById('exportModal');
        const btnExport = document.getElementById('btnExport');
        const btnClose = document.getElementById('btnCloseModal');
        const btnCancel = document.getElementById('btnCancelExport');
        const btnStart = document.getElementById('btnStartExport');
        const btnInstant = document.getElementById('btnInstantDownload');
        const instantBox = document.getElementById('instantDownloadBox');

        const showModal = () => {
            if (instantBox) {
                instantBox.style.display = this.localVideoBlob ? 'block' : 'none';
            }
            modal.style.display = 'flex';
        };

        btnExport.addEventListener('click', showModal);
        btnClose.addEventListener('click', () => modal.style.display = 'none');
        btnCancel.addEventListener('click', () => modal.style.display = 'none');

        // Instant MP4 Download button
        if (btnInstant) {
            btnInstant.addEventListener('click', () => {
                if (this.localVideoBlob) {
                    const url = window.URL.createObjectURL(this.localVideoBlob);
                    const a = document.createElement('a');
                    a.href = url;
                    a.download = `WriteStudio_Lesson_${Date.now()}.mp4`;
                    document.body.appendChild(a);
                    a.click();
                    a.remove();
                    modal.style.display = 'none';
                } else {
                    alert('No recording found yet. Click ● RECORD to create a video.');
                }
            });
        }

        btnStart.addEventListener('click', async () => {
            btnStart.disabled = true;
            const progressContainer = document.getElementById('exportProgressContainer');
            const progressBar = document.getElementById('exportProgressBar');
            const statusText = document.getElementById('exportStatusText');

            progressContainer.style.display = 'block';
            progressBar.style.width = '20%';
            statusText.textContent = 'Packaging session tracks and rasterizing vector strokes...';

            let progressPct = 20;
            const progressTimer = setInterval(() => {
                if (progressPct < 90) {
                    progressPct += 5;
                    progressBar.style.width = `${progressPct}%`;
                    if (progressPct >= 40 && progressPct < 70) {
                        statusText.textContent = 'FFmpeg encoding video & audio tracks...';
                    } else if (progressPct >= 70) {
                        statusText.textContent = 'Finalizing MP4 container...';
                    }
                }
            }, 300);

            let durationStr = this.getElapsedSessionTime();
            if (durationStr === '00:00:00' || durationStr === '00:00:00.000') {
                durationStr = '00:00:05.000';
            }

            const resVal = document.getElementById('exportResolution').value.split('x');
            const targetW = parseInt(resVal[0], 10) || 1280;
            const targetH = parseInt(resVal[1], 10) || 720;
            const targetFps = parseInt(document.getElementById('exportFps').value, 10) || 30;

            const payload = {
                sessionId: this.generateGuid(),
                metadata: {
                    title: 'Interactive Studio Lesson',
                    author: 'Presenter',
                    canvasWidth: targetW,
                    canvasHeight: targetH,
                    targetFps: targetFps,
                    duration: durationStr,
                    totalPages: this.pages.length,
                    hasAudioTrack: !!this.recordedAudioBlob,
                    hasWebcamTrack: !!this.recordedCameraBlob
                },
                pages: this.pages.map(page => ({
                    id: this.generateGuid(),
                    index: page.index,
                    title: page.title,
                    background: page.background || 'Blackboard',
                    question: page.question ? {
                        id: page.question.id || this.generateGuid(),
                        questionNumber: page.question.questionNumber || '',
                        questionText: page.question.questionText || '',
                        options: (page.question.options || []).map(opt => ({
                            label: opt.label || '',
                            text: opt.text || ''
                        })),
                        correctAnswer: page.question.correctAnswer || '',
                        isAnswerRevealed: !!page.question.isAnswerRevealed,
                        x: page.question.x || 80,
                        y: page.question.y || 80,
                        width: page.question.width || 1000,
                        fontSize: page.question.fontSize || 26,
                        fontFamily: page.question.fontFamily || 'sans-serif'
                    } : null,
                    strokes: (page.strokes || []).map(stroke => ({
                        id: stroke.id || this.generateGuid(),
                        pageIndex: stroke.pageIndex || 0,
                        startTime: stroke.startTime || '00:00:00.000',
                        endTime: stroke.endTime || '00:00:01.000',
                        color: stroke.color || { R: 255, G: 255, B: 255, A: 255 },
                        thickness: stroke.thickness || 4,
                        opacity: stroke.opacity || 1.0,
                        toolType: stroke.toolType || 'Pen',
                        textContent: stroke.textContent || null,
                        fontSize: stroke.fontSize || 24,
                        points: (stroke.points || []).map(pt => ({
                            x: pt.x,
                            y: pt.y,
                            pressure: pt.pressure || 0.5,
                            timestamp: pt.timestamp || '00:00:00.000'
                        }))
                    }))
                })),
                events: (this.timelineEvents || []).map(evt => {
                    const clone = { ...evt };
                    if (clone.oldState === 0) clone.oldState = 'Stopped';
                    if (clone.oldState === 1) clone.oldState = 'Recording';
                    if (clone.oldState === 2) clone.oldState = 'Paused';
                    if (clone.newState === 0) clone.newState = 'Stopped';
                    if (clone.newState === 1) clone.newState = 'Recording';
                    if (clone.newState === 2) clone.newState = 'Paused';
                    return clone;
                })
            };

            const formData = new FormData();
            formData.append('session', JSON.stringify(payload));

            if (this.recordedAudioBlob) {
                formData.append('audioFile', this.recordedAudioBlob, 'recording.webm');
            }

            if (this.recordedCameraBlob) {
                formData.append('cameraFile', this.recordedCameraBlob, 'webcam.webm');
            }

            try {
                const response = await fetch('/api/export', {
                    method: 'POST',
                    body: formData
                });

                clearInterval(progressTimer);

                if (!response.ok) {
                    const errorText = await response.text();
                    throw new Error(errorText || `HTTP ${response.status}`);
                }

                progressBar.style.width = '100%';
                statusText.textContent = 'Saving to Local Storage & downloading MP4...';

                const blob = await response.blob();
                
                // Save master MP4 into Local Storage (IndexedDB)
                const thumb = this.canvas.toDataURL('image/jpeg', 0.6);
                const mp4Entry = {
                    id: this.generateGuid(),
                    title: `Master HD MP4 — ${new Date().toLocaleDateString()} ${new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`,
                    createdAt: new Date().toISOString(),
                    duration: durationStr.split('.')[0],
                    format: `MP4 (${targetW}x${targetH} @ ${targetFps}fps)`,
                    sizeBytes: blob.size,
                    blob: blob,
                    thumbnail: thumb,
                    pages: JSON.parse(JSON.stringify(this.pages)),
                    events: JSON.parse(JSON.stringify(this.timelineEvents))
                };
                await this.storage.saveRecording(mp4Entry);
                this.updateLibraryBadge();

                // Trigger direct MP4 download
                const url = window.URL.createObjectURL(blob);
                const a = document.createElement('a');
                a.href = url;
                a.download = `WriteStudio_Master_${Date.now()}.mp4`;
                document.body.appendChild(a);
                a.click();
                a.remove();

                setTimeout(() => {
                    modal.style.display = 'none';
                    progressContainer.style.display = 'none';
                    btnStart.disabled = false;
                }, 1200);
            } catch (err) {
                clearInterval(progressTimer);
                btnStart.disabled = false;
                progressContainer.style.display = 'none';

                if (this.localVideoBlob) {
                    // Directly download the complete local MP4 video
                    const url = window.URL.createObjectURL(this.localVideoBlob);
                    const a = document.createElement('a');
                    a.href = url;
                    a.download = `WriteStudio_Lesson_${Date.now()}.mp4`;
                    document.body.appendChild(a);
                    a.click();
                    a.remove();
                    modal.style.display = 'none';
                } else {
                    alert(`Export notice: ${err.message}`);
                }
            }
        });
    }

    // ==========================================
    // 📚 Recordings Library & Local Storage UI
    // ==========================================
    bindLibraryEvents() {
        const btnOpen = document.getElementById('btnOpenLibrary');
        const modal = document.getElementById('libraryModal');
        const btnClose = document.getElementById('btnCloseLibrary');
        const btnCloseFooter = document.getElementById('btnCloseLibraryFooter');
        const btnClearAll = document.getElementById('btnClearAllRecordings');

        const playerModal = document.getElementById('videoPlayerModal');
        const btnClosePlayer = document.getElementById('btnClosePlayer');
        const btnClosePlayerFooter = document.getElementById('btnClosePlayerFooter');

        btnOpen.addEventListener('click', async () => {
            await this.renderLibrary();
            modal.style.display = 'flex';
        });

        btnClose.addEventListener('click', () => modal.style.display = 'none');
        btnCloseFooter.addEventListener('click', () => modal.style.display = 'none');

        btnClearAll.addEventListener('click', async () => {
            if (confirm('Are you sure you want to delete all saved recordings from local storage?')) {
                await this.storage.clearAllRecordings();
                await this.renderLibrary();
                this.updateLibraryBadge();
            }
        });

        const closePlayer = () => {
            const player = document.getElementById('libraryVideoPlayer');
            player.pause();
            player.src = '';
            playerModal.style.display = 'none';
        };

        btnClosePlayer.addEventListener('click', closePlayer);
        btnClosePlayerFooter.addEventListener('click', closePlayer);
    }

    async updateLibraryBadge() {
        try {
            const list = await this.storage.getAllRecordings();
            const badge = document.getElementById('libraryBadge');
            if (badge) badge.textContent = list.length;
        } catch { }
    }

    async renderLibrary() {
        const listContainer = document.getElementById('recordingsList');
        const emptyNotice = document.getElementById('emptyLibraryNotice');
        const infoText = document.getElementById('storageInfoText');

        const items = await this.storage.getAllRecordings();
        this.updateLibraryBadge();

        if (items.length === 0) {
            listContainer.innerHTML = '';
            emptyNotice.style.display = 'block';
            infoText.textContent = 'Stored Locally in Browser: 0 recordings';
            return;
        }

        emptyNotice.style.display = 'none';
        let totalBytes = 0;
        listContainer.innerHTML = '';

        items.forEach(item => {
            totalBytes += item.sizeBytes || 0;
            const sizeMb = ((item.sizeBytes || 0) / (1024 * 1024)).toFixed(1);
            const dateStr = new Date(item.createdAt).toLocaleString();

            const card = document.createElement('div');
            card.className = 'recording-card';
            card.innerHTML = `
                <img class="recording-thumb" src="${item.thumbnail || ''}" alt="Lesson Thumbnail">
                <div class="recording-info">
                    <div class="recording-title">${item.title}</div>
                    <div class="recording-details">
                        <span>⏱ ${item.duration}</span>
                        <span>•</span>
                        <span>💾 ${sizeMb} MB</span>
                        <span>•</span>
                        <span>${item.format || 'MP4 Video'}</span>
                        <span>•</span>
                        <span>📅 ${dateStr}</span>
                    </div>
                </div>
                <div class="recording-actions">
                    <button class="btn btn-sm btn-primary btn-play-rec" data-id="${item.id}">▶ Play</button>
                    <button class="btn btn-sm btn-secondary btn-dl-rec" data-id="${item.id}">⬇ Download MP4</button>
                    <button class="btn btn-sm btn-danger btn-del-rec" data-id="${item.id}">🗑</button>
                </div>
            `;

            // Play Video
            card.querySelector('.btn-play-rec').addEventListener('click', () => this.playStoredVideo(item));

            // Download Video
            card.querySelector('.btn-dl-rec').addEventListener('click', () => {
                const url = URL.createObjectURL(item.blob);
                const a = document.createElement('a');
                a.href = url;
                a.download = `${item.title.replace(/[^a-zA-Z0-9_-]/g, '_')}.mp4`;
                document.body.appendChild(a);
                a.click();
                a.remove();
            });

            // Delete Video
            card.querySelector('.btn-del-rec').addEventListener('click', async () => {
                if (confirm(`Delete "${item.title}" from local storage?`)) {
                    await this.storage.deleteRecording(item.id);
                    await this.renderLibrary();
                }
            });

            listContainer.appendChild(card);
        });

        const totalMb = (totalBytes / (1024 * 1024)).toFixed(1);
        infoText.textContent = `Stored Locally in Browser: ${items.length} recordings (${totalMb} MB used)`;
    }

    playStoredVideo(item) {
        const playerModal = document.getElementById('videoPlayerModal');
        const player = document.getElementById('libraryVideoPlayer');
        const title = document.getElementById('playerModalTitle');
        const btnDl = document.getElementById('btnDownloadCurrentVideo');

        const url = URL.createObjectURL(item.blob);
        player.src = url;
        title.textContent = `▶ ${item.title} (${item.duration})`;

        btnDl.href = url;
        btnDl.download = `${item.title.replace(/[^a-zA-Z0-9_-]/g, '_')}.mp4`;

        playerModal.style.display = 'flex';
        player.play();
    }

    createPptxSlideCardElement(item, isFullscreen = false) {
        const card = document.createElement('div');
        card.className = `pptx-slide-card ${this.slideTheme === 'dark' ? 'theme-dark' : ''}`;
        
        const baseSize = this.slideFontSize || 16;
        const titleSize = isFullscreen ? baseSize + 8 : baseSize + 4;
        const textSize = isFullscreen ? baseSize + 2 : baseSize;
        
        card.style.setProperty('--pptx-title-size', `${titleSize}px`);
        card.style.setProperty('--pptx-text-size', `${textSize}px`);

        if (item.title) {
            const titleEl = document.createElement('div');
            titleEl.className = 'pptx-title';
            titleEl.textContent = item.title;
            card.appendChild(titleEl);
        }

        if (item.bullets && item.bullets.length > 0) {
            const ul = document.createElement('ul');
            ul.className = 'pptx-bullets';
            item.bullets.forEach(b => {
                const li = document.createElement('li');
                li.className = `pptx-bullet-item pptx-bullet-lvl-${Math.min(b.level, 2)}`;
                li.textContent = b.text;
                ul.appendChild(li);
            });
            card.appendChild(ul);
        }

        // Tables
        if (item.tables && item.tables.length > 0) {
            item.tables.forEach(tbl => {
                const tableEl = document.createElement('table');
                tableEl.className = 'pptx-table';
                tbl.forEach((row, rIdx) => {
                    const tr = document.createElement('tr');
                    row.forEach(cell => {
                        const cellEl = document.createElement(rIdx === 0 ? 'th' : 'td');
                        cellEl.textContent = cell;
                        tr.appendChild(cellEl);
                    });
                    tableEl.appendChild(tr);
                });
                card.appendChild(tableEl);
            });
        }

        // Embedded Images / Diagrams
        if (item.images && item.images.length > 0) {
            const imgGrid = document.createElement('div');
            imgGrid.className = 'pptx-images-grid';
            item.images.forEach(imgData => {
                const imgEl = document.createElement('img');
                imgEl.className = 'pptx-image-item';
                imgEl.src = imgData.url;
                imgEl.alt = imgData.name;
                imgEl.title = 'Click to open diagram';
                imgEl.addEventListener('click', () => window.open(imgData.url, '_blank'));
                imgGrid.appendChild(imgEl);
            });
            card.appendChild(imgGrid);
        }

        return card;
    }

    formatSpeakerNotesHtml(notesText) {
        if (!notesText || typeof notesText !== 'string') return '';
        const trimmed = notesText.trim();
        if (!trimmed) return '';

        const escapeHtml = (str) => {
            return str
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#039;');
        };

        const linkify = (text) => {
            const urlRegex = /(https?:\/\/[^\s<]+[^<.,:;"')\]\s])/g;
            return text.replace(urlRegex, (url) => {
                return `<a href="${url}" target="_blank" rel="noopener noreferrer" class="pptx-note-link" title="Open reference link in new tab">🔗 ${url}</a>`;
            });
        };

        let rawParagraphs = trimmed.split(/\r?\n+/).map(p => p.trim()).filter(p => p.length > 0);

        // Fallback: If joined as a single long block, segment on Ref: URLs or sentence clusters
        if (rawParagraphs.length === 1 && rawParagraphs[0].length > 80) {
            let fullText = rawParagraphs[0];
            const sections = [];
            
            const refMatch = fullText.match(/^Ref:\s*https?:\/\/[^\s]+/i);
            if (refMatch) {
                sections.push(refMatch[0]);
                fullText = fullText.slice(refMatch[0].length).trim();
            }

            if (fullText) {
                const sentenceMatches = fullText.match(/[^.!?]+[.!?]+(\s+|$)|[^.!?]+$/g);
                if (sentenceMatches && sentenceMatches.length > 1) {
                    let acc = '';
                    sentenceMatches.forEach(st => {
                        const s = st.trim();
                        if (acc && (acc.length + s.length > 140)) {
                            sections.push(acc);
                            acc = s;
                        } else {
                            acc = acc ? `${acc} ${s}` : s;
                        }
                    });
                    if (acc) sections.push(acc);
                } else {
                    sections.push(fullText);
                }
            }

            if (sections.length > 1) {
                rawParagraphs = sections;
            }
        }

        return rawParagraphs.map(p => {
            const escaped = escapeHtml(p);
            const linked = linkify(escaped);
            if (/^Ref:\s*/i.test(p)) {
                return `<div class="pptx-note-ref-badge">${linked}</div>`;
            }
            return `<p class="pptx-note-paragraph">${linked}</p>`;
        }).join('');
    }

    openSlideFullscreenModal(item) {
        const modal = document.getElementById('slideFullscreenModal');
        const modalTitle = document.getElementById('slideModalTitle');
        const modalBody = document.getElementById('slideModalBody');
        const dialog = document.getElementById('slideFullscreenDialog') || (modal ? modal.querySelector('.modal-dialog') : null);
        const header = document.getElementById('slideModalHeader') || (dialog ? dialog.querySelector('.modal-header') : null);
        const themeBtn = document.getElementById('btnToggleModalSlideTheme');
        const btnPrev = document.getElementById('btnModalPrevSlide');
        const btnNext = document.getElementById('btnModalNextSlide');
        if (!modal || !modalBody) return;

        if (dialog && header && !dialog.dataset.dragBound) {
            dialog.dataset.dragBound = 'true';
            header.classList.add('modal-header-draggable');
            this.makeDialogDraggable(dialog, header);
        }

        modalTitle.textContent = item.type === 'pptx'
            ? `📊 ${item.presentationName || 'PowerPoint'} — Slide ${item.slideNumber} of ${item.totalSlidesInDeck}`
            : `📑 ${item.name} (High Definition)`;

        modalBody.innerHTML = '';

        if (item.type === 'pptx') {
            if (themeBtn) themeBtn.style.display = 'inline-flex';
            const card = this.createPptxSlideCardElement(item, true);
            modalBody.appendChild(card);

            if (item.speakerNotes && item.speakerNotes.trim().length > 0) {
                const notesBox = document.createElement('div');
                notesBox.className = 'pptx-notes-box';
                notesBox.style.marginTop = '16px';
                notesBox.style.width = '100%';
                notesBox.innerHTML = `
                    <div class="pptx-notes-header">
                        <div class="pptx-notes-label">🎙️ Presenter / Speaker Notes (Private)</div>
                    </div>
                    <div class="pptx-notes-content">${this.formatSpeakerNotesHtml(item.speakerNotes)}</div>
                `;
                modalBody.appendChild(notesBox);
            }
        } else if (item.type === 'image') {
            if (themeBtn) themeBtn.style.display = 'none';
            const img = document.createElement('img');
            img.src = item.url;
            img.className = 'current-slide-img';
            modalBody.appendChild(img);
        } else if (item.type === 'pdf') {
            if (themeBtn) themeBtn.style.display = 'none';
            const iframe = document.createElement('iframe');
            iframe.src = item.url;
            iframe.className = 'current-slide-frame';
            modalBody.appendChild(iframe);
        } else if (item.type === 'text') {
            if (themeBtn) themeBtn.style.display = 'none';
            const pre = document.createElement('pre');
            pre.className = 'current-slide-text';
            pre.textContent = item.textContent || '';
            modalBody.appendChild(pre);
        } else {
            if (themeBtn) themeBtn.style.display = 'none';
            const card = document.createElement('div');
            card.className = 'pptx-slide-card';
            card.innerHTML = `
                <div class="pptx-title">📁 ${item.name}</div>
                <div style="font-size:14px; color:#94A3B8; margin:12px 0;">File size: ${(item.sizeBytes / 1024).toFixed(1)} KB</div>
                <div style="display:flex; flex-wrap:wrap; gap:8px;">
                    <button id="btnModalParsePpt" class="btn btn-sm btn-primary">📊 Parse as PowerPoint</button>
                    <button id="btnModalExtractText" class="btn btn-sm btn-secondary">📝 Extract Text</button>
                    ${item.url ? `<a href="${item.url}" download="${item.name}" class="btn btn-sm btn-secondary">⬇ Download</a>` : ''}
                </div>
            `;
            modalBody.appendChild(card);

            const btnParse = card.querySelector('#btnModalParsePpt');
            if (btnParse && item.rawFile) {
                btnParse.addEventListener('click', async () => {
                    btnParse.textContent = '⏳ Parsing Slides...';
                    const file = item.rawFile;
                    const idx = this.currentSlideIndex;
                    const ext = file.name.split('.').pop().toLowerCase();
                    if (ext === 'ppt') await this.loadPptBinaryPresentation(file);
                    else await this.loadPptxPresentation(file);
                    this.removeSlide(idx);
                    if (this.currentSlideIndex >= 0 && this.slides[this.currentSlideIndex]) {
                        this.openSlideFullscreenModal(this.slides[this.currentSlideIndex]);
                    } else {
                        modal.style.display = 'none';
                    }
                });
            }

            const btnText = card.querySelector('#btnModalExtractText');
            if (btnText && item.rawFile) {
                btnText.addEventListener('click', async () => {
                    btnText.textContent = '⏳ Extracting Text...';
                    const file = item.rawFile;
                    const idx = this.currentSlideIndex;
                    await this.loadPptBinaryPresentation(file);
                    this.removeSlide(idx);
                    if (this.currentSlideIndex >= 0 && this.slides[this.currentSlideIndex]) {
                        this.openSlideFullscreenModal(this.slides[this.currentSlideIndex]);
                    } else {
                        modal.style.display = 'none';
                    }
                });
            }
        }

        // Update modal navigation button states
        if (btnPrev) btnPrev.disabled = this.currentSlideIndex <= 0;
        if (btnNext) btnNext.disabled = this.currentSlideIndex >= this.slides.length - 1;

        modal.style.display = 'block';

        if (dialog && !dialog.dataset.dragged) {
            const dialogWidth = Math.min(920, Math.round(window.innerWidth * 0.96));
            const left = Math.max(8, Math.round((window.innerWidth - dialogWidth) / 2));
            const top = Math.max(16, Math.min(48, Math.round((window.innerHeight - 600) / 2)));
            dialog.style.position = 'fixed';
            dialog.style.left = `${left}px`;
            dialog.style.top = `${top}px`;
            dialog.style.right = 'auto';
            dialog.style.bottom = 'auto';
            dialog.style.margin = '0';
            dialog.style.transform = 'none';
        }
    }

    makeDialogDraggable(dialog, header) {
        if (!dialog || !header) return;

        let isDragging = false;
        let startX = 0, startY = 0;
        let initialLeft = 0, initialTop = 0;

        const onDragMove = (e) => {
            if (!isDragging) return;
            if (e.cancelable) e.preventDefault();

            const clientX = e.clientX !== undefined ? e.clientX : (e.touches && e.touches[0] ? e.touches[0].clientX : startX);
            const clientY = e.clientY !== undefined ? e.clientY : (e.touches && e.touches[0] ? e.touches[0].clientY : startY);

            const dx = clientX - startX;
            const dy = clientY - startY;

            let newLeft = initialLeft + dx;
            let newTop = initialTop + dy;

            const maxLeft = Math.max(0, window.innerWidth - 120);
            const maxTop = Math.max(0, window.innerHeight - 50);
            newLeft = Math.max(-dialog.offsetWidth + 120, Math.min(newLeft, maxLeft));
            newTop = Math.max(0, Math.min(newTop, maxTop));

            dialog.style.position = 'fixed';
            dialog.style.left = `${newLeft}px`;
            dialog.style.top = `${newTop}px`;
            dialog.style.right = 'auto';
            dialog.style.bottom = 'auto';
            dialog.style.margin = '0';
            dialog.style.transform = 'none';
        };

        const onDragEnd = (e) => {
            if (isDragging) {
                isDragging = false;
                header.classList.remove('dragging');
                document.body.style.userSelect = '';

                window.removeEventListener('pointermove', onDragMove);
                window.removeEventListener('pointerup', onDragEnd);
                window.removeEventListener('pointercancel', onDragEnd);
                window.removeEventListener('mousemove', onDragMove);
                window.removeEventListener('mouseup', onDragEnd);
                window.removeEventListener('touchmove', onDragMove);
                window.removeEventListener('touchend', onDragEnd);
            }
        };

        const onDragStart = (e) => {
            // Do not drag when clicking on interactive controls
            if (e.target.closest('button') || e.target.closest('.btn-close') || e.target.closest('select') || e.target.closest('input') || e.target.closest('a')) {
                return;
            }

            isDragging = true;
            startX = e.clientX !== undefined ? e.clientX : (e.touches && e.touches[0] ? e.touches[0].clientX : 0);
            startY = e.clientY !== undefined ? e.clientY : (e.touches && e.touches[0] ? e.touches[0].clientY : 0);

            const rect = dialog.getBoundingClientRect();
            initialLeft = rect.left;
            initialTop = rect.top;

            dialog.style.position = 'fixed';
            dialog.style.left = `${initialLeft}px`;
            dialog.style.top = `${initialTop}px`;
            dialog.style.right = 'auto';
            dialog.style.bottom = 'auto';
            dialog.style.margin = '0';
            dialog.style.transform = 'none';
            dialog.dataset.dragged = 'true';

            header.classList.add('dragging');
            document.body.style.userSelect = 'none';

            window.addEventListener('pointermove', onDragMove, { passive: false });
            window.addEventListener('pointerup', onDragEnd);
            window.addEventListener('pointercancel', onDragEnd);
            window.addEventListener('mousemove', onDragMove);
            window.addEventListener('mouseup', onDragEnd);
            window.addEventListener('touchmove', onDragMove, { passive: false });
            window.addEventListener('touchend', onDragEnd);
        };

        header.addEventListener('pointerdown', onDragStart);
        header.addEventListener('mousedown', onDragStart);
        header.addEventListener('touchstart', onDragStart, { passive: true });
    }

    bindSlideModalEvents() {
        const modal = document.getElementById('slideFullscreenModal');
        const dialog = document.getElementById('slideFullscreenDialog') || (modal ? modal.querySelector('.modal-dialog') : null);
        const header = document.getElementById('slideModalHeader') || (dialog ? dialog.querySelector('.modal-header') : null);
        const btnClose = document.getElementById('btnCloseSlideModal');
        const btnCloseFooter = document.getElementById('btnCloseSlideModalFooter');
        const btnTheme = document.getElementById('btnToggleModalSlideTheme');
        const btnPrev = document.getElementById('btnModalPrevSlide');
        const btnNext = document.getElementById('btnModalNextSlide');
        const btnOpenHd = document.getElementById('btnOpenHdSlideView');

        if (dialog && header && !dialog.dataset.dragBound) {
            dialog.dataset.dragBound = 'true';
            header.classList.add('modal-header-draggable');
            this.makeDialogDraggable(dialog, header);
        }

        const closeModal = () => {
            if (modal) modal.style.display = 'none';
        };

        if (btnClose) btnClose.addEventListener('click', closeModal);
        if (btnCloseFooter) btnCloseFooter.addEventListener('click', closeModal);
        if (modal) {
            modal.addEventListener('click', (e) => {
                if (e.target === modal) closeModal();
            });
        }
        window.addEventListener('keydown', (e) => {
            if (e.key === 'Escape' && modal && modal.style.display !== 'none') {
                closeModal();
            }
        });

        if (btnOpenHd) {
            btnOpenHd.addEventListener('click', () => {
                if (this.currentSlideIndex >= 0 && this.slides[this.currentSlideIndex]) {
                    this.openSlideFullscreenModal(this.slides[this.currentSlideIndex]);
                }
            });
        }

        if (btnTheme) {
            btnTheme.addEventListener('click', () => {
                this.slideTheme = this.slideTheme === 'dark' ? 'light' : 'dark';
                if (this.currentSlideIndex >= 0 && this.slides[this.currentSlideIndex]) {
                    this.openSlideFullscreenModal(this.slides[this.currentSlideIndex]);
                    this.updateSlideView();
                }
            });
        }

        if (btnPrev) {
            btnPrev.addEventListener('click', () => {
                if (this.currentSlideIndex > 0) {
                    this.currentSlideIndex--;
                    this.updateSlideView();
                    this.openSlideFullscreenModal(this.slides[this.currentSlideIndex]);
                }
            });
        }

        if (btnNext) {
            btnNext.addEventListener('click', () => {
                if (this.currentSlideIndex < this.slides.length - 1) {
                    this.currentSlideIndex++;
                    this.updateSlideView();
                    this.openSlideFullscreenModal(this.slides[this.currentSlideIndex]);
                }
            });
        }
    }

    // Utilities
    hexToRgba(hex, alpha = 1.0) {
        hex = hex.replace('#', '');
        const r = parseInt(hex.substring(0, 2), 16);
        const g = parseInt(hex.substring(2, 4), 16);
        const b = parseInt(hex.substring(4, 6), 16);
        const a = Math.round(alpha * 255);
        return { R: r, G: g, B: b, A: a };
    }

    capitalize(str) {
        return str.charAt(0).toUpperCase() + str.slice(1);
    }

    generateGuid() {
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function(c) {
            const r = Math.random() * 16 | 0, v = c === 'x' ? r : (r & 0x3 | 0x8);
            return v.toString(16);
        });
    }
}

// Bootstrap on DOM load
window.addEventListener('DOMContentLoaded', () => {
    window.studio = new WriteStudioEngine();
});
