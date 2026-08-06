import uvicorn
uvicorn.run('google_flow_ext.api.app:app', host='127.0.0.1', port=8787, log_level='info')
