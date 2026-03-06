/**
 * fcMobile — Mobile-first enhancements
 * Pull-to-refresh, card swipe, offline detection, bottom sheets, lazy images.
 * v1 — 2026-03
 */
'use strict';

(function () {

    var fcMobile = {

        // ─────────────────────────────────────────────────────────────────────
        // 1. Offline detection
        // ─────────────────────────────────────────────────────────────────────
        initOfflineDetection: function () {
            var banner = document.getElementById('fc-offline-banner');
            if (!banner) return;

            var hideTimer = null;
            var backOnlineText = banner.querySelector('.fc-offline-text');

            function showOffline() {
                clearTimeout(hideTimer);
                document.body.classList.add('fc-is-offline');
                banner.classList.remove('fc-offline-banner--back-online');
                if (backOnlineText) backOnlineText.textContent = 'No internet connection \u2014 showing cached data';
                var dot = banner.querySelector('.fc-offline-dot');
                if (dot) dot.style.background = '#ef4444';
                banner.querySelector('.fc-offline-cached').textContent = 'Offline';
                banner.classList.add('fc-offline-banner--visible');
            }

            function showBackOnline() {
                clearTimeout(hideTimer);
                document.body.classList.remove('fc-is-offline');
                banner.classList.add('fc-offline-banner--back-online');
                if (backOnlineText) backOnlineText.textContent = 'Back online';
                var dot = banner.querySelector('.fc-offline-dot');
                if (dot) dot.style.background = '#fff';
                banner.querySelector('.fc-offline-cached').textContent = 'Connected';
                banner.classList.add('fc-offline-banner--visible');
                hideTimer = setTimeout(function () {
                    banner.classList.remove('fc-offline-banner--visible', 'fc-offline-banner--back-online');
                }, 2800);
            }

            if (!navigator.onLine) showOffline();
            window.addEventListener('offline', showOffline);
            window.addEventListener('online', showBackOnline);
        },

        // ─────────────────────────────────────────────────────────────────────
        // 2. Lazy image loading via IntersectionObserver
        // ─────────────────────────────────────────────────────────────────────
        initLazyImages: function () {
            if (!('IntersectionObserver' in window)) {
                // Fallback: just load all lazy images immediately
                document.querySelectorAll('img[data-src]').forEach(function (img) {
                    img.src = img.dataset.src;
                    img.removeAttribute('data-src');
                    img.classList.add('is-loaded');
                });
                return;
            }

            var observer = new IntersectionObserver(function (entries) {
                entries.forEach(function (entry) {
                    if (!entry.isIntersecting) return;
                    var img = entry.target;
                    if (img.dataset.src) {
                        img.src = img.dataset.src;
                        img.removeAttribute('data-src');
                        img.onload = function () { img.classList.add('is-loaded'); };
                    } else {
                        img.classList.add('is-loaded');
                    }
                    observer.unobserve(img);
                });
            }, { rootMargin: '100px 0px' });

            document.querySelectorAll('img.fc-lazy, img[data-src]').forEach(function (img) {
                observer.observe(img);
            });
        },

        // ─────────────────────────────────────────────────────────────────────
        // 3. Pull-to-refresh
        // ─────────────────────────────────────────────────────────────────────
        initPullToRefresh: function (contentEl, dotNetRef) {
            if (!contentEl) return;
            if (window.matchMedia('(min-width: 641px)').matches) return;

            // Inject indicator node if not present
            var indicator = contentEl.querySelector('.fc-ptr-indicator');
            if (!indicator) {
                indicator = document.createElement('div');
                indicator.className = 'fc-ptr-indicator';
                indicator.setAttribute('aria-hidden', 'true');
                indicator.innerHTML =
                    '<svg class="fc-ptr-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
                    'stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" width="20" height="20" aria-hidden="true">' +
                    '<polyline points="23 4 23 10 17 10"/>' +
                    '<path d="M20.49 15a9 9 0 11-2.12-9.36L23 10"/>' +
                    '</svg>';
                contentEl.style.position = 'relative';
                contentEl.insertBefore(indicator, contentEl.firstChild);
            }

            var startY = 0;
            var startScrollTop = 0;
            var currentY = 0;
            var pulling = false;
            var triggered = false;
            var THRESHOLD = 72;
            var MAX_PULL = 100;

            contentEl.addEventListener('touchstart', function (e) {
                startScrollTop = contentEl.scrollTop;
                if (startScrollTop > 0) { pulling = false; return; }
                startY = e.touches[0].clientY;
                pulling = true;
                triggered = false;
            }, { passive: true });

            contentEl.addEventListener('touchmove', function (e) {
                if (!pulling) return;
                if (contentEl.scrollTop > 0) { pulling = false; return; }
                currentY = e.touches[0].clientY;
                var dy = currentY - startY;
                if (dy <= 0) return;

                var pullAmount = Math.min(dy * 0.5, MAX_PULL);
                var progress = Math.min(pullAmount / THRESHOLD, 1);

                indicator.style.top = (-48 + pullAmount) + 'px';
                indicator.style.opacity = progress.toString();
                indicator.querySelector('.fc-ptr-icon').style.transform =
                    'rotate(' + (progress * 360) + 'deg)';

                if (dy > THRESHOLD && !triggered) {
                    triggered = true;
                    if (navigator.vibrate) navigator.vibrate(10);
                }
            }, { passive: true });

            contentEl.addEventListener('touchend', function () {
                if (!pulling) return;
                pulling = false;

                var dy = currentY - startY;
                if (dy > THRESHOLD) {
                    // Show active spinner, then fire refresh
                    indicator.style.top = '12px';
                    indicator.style.opacity = '1';
                    indicator.querySelector('.fc-ptr-icon').style.animation =
                        'fcPtrSpin 0.7s linear infinite';

                    // Dispatch custom event — pages can listen
                    document.dispatchEvent(new CustomEvent('fc:pull-refresh'));

                    // Optionally call .NET
                    if (dotNetRef) {
                        try { dotNetRef.invokeMethodAsync('OnPullToRefresh'); } catch (_) { }
                    }

                    setTimeout(function () {
                        indicator.style.top = '';
                        indicator.style.opacity = '';
                        indicator.querySelector('.fc-ptr-icon').style.animation = '';
                    }, 1400);
                } else {
                    indicator.style.top = '';
                    indicator.style.opacity = '';
                    indicator.querySelector('.fc-ptr-icon').style.transform = '';
                }
            }, { passive: true });
        },

        // ─────────────────────────────────────────────────────────────────────
        // 4. Card swipe gestures (DataTable mobile cards)
        // ─────────────────────────────────────────────────────────────────────
        initCardSwipe: function (tableId, dotNetRef) {
            var wrapper = document.getElementById(tableId);
            if (!wrapper) return;
            var cardsContainer = wrapper.querySelector('.fc-dt-cards');
            if (!cardsContainer) return;

            cardsContainer.querySelectorAll('.fc-dt-card[data-swipeable]').forEach(function (card, idx) {
                var startX = 0, startY = 0, dx = 0;
                var isHorizontal = null;
                var THRESHOLD = 72;
                var MAX_X = 120;
                var animating = false;

                card.addEventListener('touchstart', function (e) {
                    if (animating) return;
                    startX = e.touches[0].clientX;
                    startY = e.touches[0].clientY;
                    dx = 0;
                    isHorizontal = null;
                    card.style.transition = 'none';
                }, { passive: true });

                card.addEventListener('touchmove', function (e) {
                    if (animating) return;
                    var cx = e.touches[0].clientX;
                    var cy = e.touches[0].clientY;
                    dx = cx - startX;
                    var dy = cy - startY;

                    if (isHorizontal === null) {
                        isHorizontal = Math.abs(dx) > Math.abs(dy) + 4;
                    }
                    if (!isHorizontal) return;

                    var clamped = Math.max(-MAX_X, Math.min(MAX_X, dx));
                    card.style.transform = 'translateX(' + clamped + 'px)';
                    card.classList.toggle('is-swiping-right', dx > 16);
                    card.classList.toggle('is-swiping-left', dx < -16);
                }, { passive: true });

                card.addEventListener('touchend', function () {
                    if (animating) return;
                    card.style.transition = '';

                    if (dx > THRESHOLD) {
                        // Swipe right: quick action
                        animating = true;
                        card.style.transform = 'translateX(110%)';
                        setTimeout(function () {
                            card.style.transform = '';
                            card.classList.remove('is-swiping-right', 'is-swiping-left');
                            animating = false;
                            if (dotNetRef) {
                                try { dotNetRef.invokeMethodAsync('OnMobileSwipeRight', idx); } catch (_) { }
                            }
                        }, 200);
                    } else if (dx < -THRESHOLD) {
                        // Swipe left: delete action
                        animating = true;
                        card.style.transform = 'translateX(-110%)';
                        setTimeout(function () {
                            card.style.transform = '';
                            card.classList.remove('is-swiping-right', 'is-swiping-left');
                            animating = false;
                            if (dotNetRef) {
                                try { dotNetRef.invokeMethodAsync('OnMobileSwipeLeft', idx); } catch (_) { }
                            }
                        }, 200);
                    } else {
                        card.style.transform = '';
                        card.classList.remove('is-swiping-right', 'is-swiping-left');
                    }
                }, { passive: true });
            });
        },

        // ─────────────────────────────────────────────────────────────────────
        // 5. Bottom sheet open / close
        // ─────────────────────────────────────────────────────────────────────
        openSheet: function (sheetId) {
            var overlay = document.getElementById(sheetId);
            if (!overlay) return;
            document.body.style.overflow = 'hidden';
            overlay.removeAttribute('hidden');
            // Force reflow then animate
            overlay.getBoundingClientRect();
            overlay.classList.add('is-open');

            // Close on Escape
            function onKey(e) {
                if (e.key === 'Escape') {
                    fcMobile.closeSheet(sheetId);
                    window.removeEventListener('keydown', onKey);
                }
            }
            window.addEventListener('keydown', onKey);

            // Trap focus inside sheet
            var sheet = overlay.querySelector('.fc-sheet');
            var focusable = sheet ? sheet.querySelectorAll(
                'a[href], button:not([disabled]), input:not([disabled]), [tabindex]:not([tabindex="-1"])'
            ) : [];
            if (focusable.length) focusable[0].focus();
        },

        closeSheet: function (sheetId) {
            var overlay = document.getElementById(sheetId);
            if (!overlay) return;
            overlay.classList.remove('is-open');
            document.body.style.overflow = '';
            setTimeout(function () {
                if (!overlay.classList.contains('is-open')) {
                    overlay.setAttribute('hidden', '');
                }
            }, 350);
        },

        // ─────────────────────────────────────────────────────────────────────
        // 6. Auto-init
        // ─────────────────────────────────────────────────────────────────────
        init: function () {
            this.initOfflineDetection();
            this.initLazyImages();
        }
    };

    window.fcMobile = fcMobile;

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { fcMobile.init(); });
    } else {
        fcMobile.init();
    }

})();
