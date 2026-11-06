/* Chrome DevTools Protocol smoke checks: demo data only, never a real database. */
const fs = require("fs");

async function main() {
  const [port, url, desktopImage, mobileImage] = process.argv.slice(2);
  const target = (await fetch("http://127.0.0.1:" + port + "/json/list")
    .then(response => response.json())).find(item => item.type === "page");
  if (!target) throw new Error("Chrome CDP page missing");

  const socket = new WebSocket(target.webSocketDebuggerUrl);
  const pending = new Map();
  const exceptions = [];
  let seq = 0, checks = 0;
  socket.onmessage = event => {
    const message = JSON.parse(event.data);
    if (message.method === "Runtime.exceptionThrown")
      exceptions.push(message.params.exceptionDetails.exception?.description ||
        message.params.exceptionDetails.text);
    if (!message.id || !pending.has(message.id)) return;
    const item = pending.get(message.id);
    pending.delete(message.id);
    message.error ? item.reject(new Error(message.error.message)) : item.resolve(message.result);
  };
  await new Promise((resolve, reject) => {
    socket.addEventListener("open", resolve, {once:true});
    socket.addEventListener("error", reject, {once:true});
  });
  function send(method, params = {}) {
    const id = ++seq;
    return new Promise((resolve, reject) => {
      pending.set(id, {resolve, reject});
      socket.send(JSON.stringify({id, method, params}));
    });
  }
  async function evalJs(expression) {
    const response = await send("Runtime.evaluate",
      {expression, returnByValue:true, awaitPromise:true});
    if (response.exceptionDetails)
      throw new Error(response.exceptionDetails.exception?.description ||
        response.exceptionDetails.text);
    return response.result.value;
  }
  function check(ok, label) {
    if (!ok) throw new Error("FAIL: " + label);
    console.log("PASS: " + label);
    checks++;
  }
  async function waitFor(expression, label) {
    for (let i = 0; i < 40; ++i) {
      if (await evalJs(expression)) return;
      await new Promise(resolve => setTimeout(resolve, 90));
    }
    throw new Error("Timed out: " + label);
  }
  async function screenshot(file) {
    const result = await send("Page.captureScreenshot",
      {format:"png", captureBeyondViewport:false, fromSurface:true});
    fs.writeFileSync(file, Buffer.from(result.data,"base64"));
    console.log("SCREENSHOT: " + file + " bytes=" + fs.statSync(file).size);
  }

  await send("Page.enable");
  await send("Runtime.enable");
  await send("Emulation.setDeviceMetricsOverride",
    {width:1440,height:940,deviceScaleFactor:1,mobile:false});
  await send("Page.navigate",{url});
  await waitFor('document.querySelector("#copy-count")?.textContent === "3"',
    "initial mock data");
  check(await evalJs('document.querySelectorAll(".circ-tab").length') === 4,
    "four workbench tabs");
  check(await evalJs('document.querySelector("#loan-count").textContent') === "1",
    "active loan count");
  await evalJs('document.querySelector("#__circulation_demo_badge").style.display="none"');
  await screenshot(desktopImage);

  await evalJs('document.querySelector("#tab-readers").click(); document.querySelectorAll("#readers-body tr")[1].querySelector(".circ-action").click()');
  check(await evalJs('document.querySelector("#reader-name").value') === "演示读者乙",
    "edit reader opens with current values");
  await evalJs('document.querySelector("#reader-name").value="演示读者乙·修订"; document.querySelector("#reader-form").requestSubmit()');
  await waitFor('document.querySelectorAll("#readers-body tr")[1].textContent.includes("修订")',
    "reader edit persisted in mock memory");
  check(await evalJs('document.querySelector("#reader-edit-cancel").hidden') === true,
    "reader form resets to create mode");

  await evalJs('window.confirm=()=>true; document.querySelectorAll("#readers-body tr")[0].querySelectorAll(".circ-action")[1].click()');
  await waitFor('document.querySelector("#circ-banner").classList.contains("is-error")',
    "active loan prevents disabling reader");
  check(await evalJs('window.__circulationDemo.readers[0].IsActive') === true,
    "failed deactivation preserves reader state");

  await evalJs('document.querySelectorAll("#readers-body tr")[1].querySelectorAll(".circ-action")[1].click()');
  await waitFor("window.__circulationDemo.readers[1].IsActive === false && document.querySelectorAll('#readers-body tr')[1].querySelectorAll('.circ-action')[1].textContent === '启用'",
    "disable idle reader and refresh controls");
  await evalJs('document.querySelectorAll("#readers-body tr")[1].querySelectorAll(".circ-action")[1].click()');
  await waitFor("window.__circulationDemo.readers[1].IsActive === true && !document.querySelector('#reservation-reader').disabled && Array.from(document.querySelector('#reservation-reader').options).some(o=>o.value==='2')",
    "reactivate reader and refresh reservation options");

  await evalJs('document.querySelector("#tab-reservations").click(); document.querySelector("#reservation-book").value="2"; document.querySelector("#reservation-reader").value="2"; document.querySelector("#reservation-form").requestSubmit()');
  await waitFor('window.__circulationDemo.reservations.length===1',
    "reservation added to queue");
  await waitFor("document.querySelector('#reservations-body').textContent.includes('演示读者乙')",
    "reservation visible after refresh");
  check(await evalJs('window.__circulationDemo.reservations[0].Status') === 0,
    "reservation queued");

  await evalJs('document.querySelector("#tab-loans").click(); document.querySelector("#loans-body .circ-action").click()');
  await waitFor('document.querySelector("#loan-count").textContent === "0"',
    "return frees borrowed copy");

  await evalJs('document.querySelector("#loan-reader").value="1"; document.querySelector("#loan-copy").value="2"; document.querySelector("#loan-form").requestSubmit()');
  await waitFor('document.querySelector("#circ-banner").classList.contains("is-error")',
    "queue priority denies another reader");
  check(await evalJs('window.__circulationDemo.reservations[0].Status') === 0,
    "failed borrow does not fulfill hold");

  await evalJs('document.querySelector("#loan-reader").value="2"; document.querySelector("#loan-copy").value="2"; document.querySelector("#loan-form").requestSubmit()');
  await waitFor('document.querySelector("#loan-count").textContent === "1"',
    "reserved reader borrows");
  check(await evalJs('window.__circulationDemo.reservations[0].Status') === 1,
    "reservation fulfilled atomically in mock workflow");

  await evalJs('document.querySelector("#loans-body .circ-action:nth-of-type(2)").click()');
  await waitFor('window.__circulationDemo.loans[0].RenewCount === 1',
    "loan renewed once");
  await waitFor("document.querySelector('#loans-body .circ-action:nth-of-type(2)')?.disabled === true",
    "second renewal disabled after refreshed loan list");

  await evalJs('document.querySelector("#only-overdue").checked=true; document.querySelector("#only-overdue").dispatchEvent(new Event("change",{bubbles:true}))');
  check(await evalJs('document.querySelector("#loans-body").children.length') === 0,
    "overdue-only empty state");
  await evalJs('document.querySelector("#only-overdue").checked=false; document.querySelector("#only-overdue").dispatchEvent(new Event("change",{bubbles:true}))');

  await evalJs('document.querySelector("#tab-reservations").click(); document.querySelector("#reservation-book").value="2"; document.querySelector("#reservation-reader").value="1"; document.querySelector("#reservation-form").requestSubmit()');
  await waitFor('window.__circulationDemo.reservations.length === 2',
    "second queued reader");
  await waitFor("document.querySelectorAll('#reservations-body .circ-action').length === 1",
    "queued reservation visible before cancel");
  await evalJs('document.querySelector("#reservations-body .circ-action").click()');
  await waitFor('window.__circulationDemo.reservations[1].Status === 2',
    "reservation cancellation");
  check(await evalJs('window.__circulationDemo.reservations.length') === 2,
    "canceled record retained for history");

  await evalJs('window.__circulationDemo.fail=true; document.querySelector("#refresh-circulation").click()');
  await waitFor('document.querySelector("#circ-banner").classList.contains("is-error")',
    "database failure banner");
  await evalJs('window.__circulationDemo.fail=false; document.querySelector("#refresh-circulation").click()');
  await waitFor('document.querySelector("#copy-count").textContent==="3"',
    "connection restored");

  await send("Emulation.setDeviceMetricsOverride",
    {width:390,height:844,deviceScaleFactor:1,mobile:true});
  await send("Page.navigate",{url});
  await waitFor('document.querySelector("#copy-count")?.textContent === "3"',
    "mobile data load");
  const mobile = JSON.parse(await evalJs(
    'JSON.stringify({viewport:innerWidth,scrollWidth:document.documentElement.scrollWidth,tabs:document.querySelectorAll(".circ-tab").length})'
  ));
  console.log("MOBILE_LAYOUT=" + JSON.stringify(mobile));
  check(mobile.viewport===390 && mobile.scrollWidth<=390 && mobile.tabs===4,
    "responsive four-tab layout without page overflow");
  await evalJs('document.querySelector("#__circulation_demo_badge").style.display="none"');
  await screenshot(mobileImage);

  check(exceptions.length === 0,"no uncaught Chrome script errors");
  console.log("TOTAL_BROWSER_CHECKS=" + checks);
  socket.close();
}

main().catch(error => {console.error(error); process.exit(1);});