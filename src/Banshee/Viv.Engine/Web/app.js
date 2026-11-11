/* ============================================================
   Viv 服务页面渲染器

   数据由服务端内联成 window.__VIV__（见 ApiStartedMiddleware），
   本文件只负责：把数据填进 DOM、探测健康状态、渲染网关服务列表。

   为什么不在服务端拼 HTML：
     · 加一个字段只改中间件里的一个属性，不用同时动模板和 Replace 链
     · JSON 序列化自带转义，前端用 textContent 写入，服务名里带 <script> 也无效
   ============================================================ */
(function () {
    'use strict';

    var state = window.__VIV__ || {};

    function qs(selector, root) { return (root || document).querySelector(selector); }
    function qsa(selector, root) { return [].slice.call((root || document).querySelectorAll(selector)); }


    /* ---------- 1. 文本绑定：<span data-bind="serviceName"> ---------- */

    function applyTextBindings() {
        qsa('[data-bind]').forEach(function (el) {
            var value = state[el.getAttribute('data-bind')];
            if (value !== undefined && value !== null && value !== '') {
                el.textContent = String(value);
            }
        });
    }


    /* ---------- 2. 运行时长 ---------- */

    function formatUptime(ms) {
        if (!isFinite(ms) || ms < 0) { ms = 0; }
        var total = Math.floor(ms / 1000);
        var days = Math.floor(total / 86400);
        var hours = Math.floor(total % 86400 / 3600);
        var minutes = Math.floor(total % 3600 / 60);
        var seconds = total % 60;
        var parts = [];
        if (days) { parts.push(days + 'd'); }
        if (hours) { parts.push(hours + 'h'); }
        if (minutes) { parts.push(minutes + 'm'); }
        if (seconds || !parts.length) { parts.push(seconds + 's'); }
        return parts.join(' ');
    }

    function startUptime() {
        var el = qs('[data-uptime]');
        if (!el || !state.startedAt) { return; }

        function tick() { el.textContent = formatUptime(Date.now() - state.startedAt); }

        tick();
        setInterval(tick, 1000);
    }


    /* ---------- 3. 健康检查 ----------
       状态点默认是中性灰（见 app.css）。打开页面就自动探一次 ——
       只写「运行中」却不去验证，等于让页面陈述一个没发生过的事实。 */

    var healthDot = qs('[data-health-dot]');
    var healthText = qs('[data-health-text]');
    var healthBtn = qs('[data-health-btn]');
    var healthLabel = healthBtn ? healthBtn.textContent : '';
    var healthBusy = false;

    function paintHealth(kind, label) {
        if (healthDot) {
            healthDot.classList.toggle('is-ok', kind === 'ok');
            healthDot.classList.toggle('is-bad', kind === 'bad');
        }
        if (healthText) { healthText.textContent = label; }
    }

    function paintHealthButton(kind, label) {
        if (!healthBtn) { return; }
        healthBtn.classList.remove('is-ok', 'is-bad');
        if (kind) { healthBtn.classList.add('is-' + kind); }
        healthBtn.textContent = label;
    }

    async function probeHealth() {
        var response = await fetch(state.healthUrl || '/health', {
            method: 'GET',
            headers: { 'Accept': 'application/json, text/plain' },
            signal: AbortSignal.timeout(5000)
        });

        if (!response.ok) { return false; }

        // 健康检查的两种产出都要认：ASP.NET 的 "Healthy" 文本，或 { status: "Healthy" }
        var contentType = response.headers.get('content-type') || '';
        if (contentType.indexOf('application/json') >= 0) {
            var payload = await response.json();
            return String(payload.status || '').toLowerCase() === 'healthy';
        }

        return (await response.text()).trim().toLowerCase() === 'healthy';
    }

    async function checkHealth(isManual) {
        if (healthBusy) { return; }
        healthBusy = true;

        if (isManual) {
            healthBtn.classList.add('is-busy');
            paintHealthButton(null, '检查中 …');
        }

        try {
            var ok = await probeHealth();
            paintHealth(ok ? 'ok' : 'bad', ok ? '运行中' : '异常');
            if (isManual) { paintHealthButton(ok ? 'ok' : 'bad', ok ? '✓ 全部正常' : '✗ 服务不健康'); }
        } catch (error) {
            paintHealth('bad', '无法连接');
            if (isManual) { paintHealthButton('bad', '✗ 连接失败'); }
        } finally {
            healthBusy = false;
            if (isManual) {
                healthBtn.classList.remove('is-busy');
                setTimeout(function () { paintHealthButton(null, healthLabel); }, 3000);
            }
        }
    }

    function initHealth() {
        if (!healthDot) { return; }

        paintHealth(null, '检测中 …');
        checkHealth(false);

        if (healthBtn) {
            healthBtn.addEventListener('click', function () { checkHealth(true); });
        }
    }


    /* ---------- 4. 网关：服务列表 ----------
       一段 JSON 数组，DOM 由这里生成；空数组时容器是 :empty，CSS 直接不显示。 */

    function renderServices() {
        var host = qs('[data-services]');
        if (!host) { return; }

        (state.services || []).forEach(function (service) {
            var link = document.createElement('a');
            link.className = 'service-tag';
            link.href = service.docsUrl || '#';
            link.target = '_blank';
            link.rel = 'noopener';

            var dot = document.createElement('span');
            dot.className = 'status';

            var arrow = document.createElement('span');
            arrow.className = 'arrow';
            arrow.textContent = '→';

            link.appendChild(dot);
            link.appendChild(document.createTextNode(service.label || service.name || ''));
            link.appendChild(arrow);
            host.appendChild(link);
        });
    }


    /* ---------- 5. 404：回显用户实际访问的路径 ---------- */

    function renderRequestPath() {
        var el = qs('[data-request-path]');
        if (el && state.requestPath) { el.textContent = state.requestPath; }
    }


    /* ---------- 6. 快捷键 ---------- */

    function bindHotkeys() {
        // D → 文档，H → 首页。输入框里不拦截，否则打字会跳页。
        var targets = { d: state.docsUrl, h: state.homeUrl };

        document.addEventListener('keydown', function (event) {
            if (event.ctrlKey || event.metaKey || event.altKey) { return; }

            var target = event.target;
            if (target && (/^(INPUT|TEXTAREA|SELECT)$/.test(target.tagName) || target.isContentEditable)) { return; }

            var url = targets[String(event.key).toLowerCase()];
            if (url) {
                event.preventDefault();
                window.location.href = url;
            }
        });
    }


    /* ---------- 启动 ---------- */

    applyTextBindings();
    startUptime();
    renderServices();
    renderRequestPath();
    bindHotkeys();
    initHealth();

    console.log('%c⚡ Viv', 'font-size:20px;font-weight:700;color:#d9ff00;', state.serviceName || '');
    console.log('%c📖 D 打开 API 文档 · H 回到首页', 'font-size:12px;color:#888;');
})();
