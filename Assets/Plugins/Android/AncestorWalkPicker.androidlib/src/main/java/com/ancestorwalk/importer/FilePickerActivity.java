package com.ancestorwalk.importer;

import android.app.Activity;
import android.content.Intent;
import android.database.Cursor;
import android.net.Uri;
import android.os.Bundle;
import android.provider.OpenableColumns;
import java.io.*;
import java.util.UUID;

// The system grants only the selected document. No broad storage permission is requested.
public final class FilePickerActivity extends Activity {
    private String callback;
    private boolean sent;
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        callback=getIntent().getStringExtra("callback");
        if(saved!=null)return;
        Intent intent=new Intent(Intent.ACTION_OPEN_DOCUMENT);
        intent.addCategory(Intent.CATEGORY_OPENABLE);
        intent.setType(getIntent().getBooleanExtra("photos",false)?"image/*":"*/*");
        intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        try { startActivityForResult(intent,1); }
        catch(Exception e) { complete("ERROR:No system document picker. Use Import inbox."); }
    }
    @Override protected void onActivityResult(int request,int result,Intent data) {
        super.onActivityResult(request,result,data);
        if(request!=1)return;
        if(result!=RESULT_OK||data==null||data.getData()==null){complete("");return;}
        final Uri uri=data.getData();
        new Thread(()->{
            File output=null;
            try {
                String name="document";
                try(Cursor c=getContentResolver().query(uri,new String[]{OpenableColumns.DISPLAY_NAME},null,null,null)) {
                    if(c!=null&&c.moveToFirst())name=c.getString(0);
                }
                name=name.replaceAll("[^a-zA-Z0-9._-]","_");
                if(name.length()>120)name=name.substring(name.length()-120);
                File dir=new File(getFilesDir(),"ImportInbox");dir.mkdirs();
                output=new File(dir,UUID.randomUUID().toString()+"-"+name);
                try(InputStream input=getContentResolver().openInputStream(uri);OutputStream target=new FileOutputStream(output)) {
                    if(input==null)throw new IOException("Cannot open selected file");
                    byte[] buffer=new byte[65536];long total=0;int count;
                    while((count=input.read(buffer))!=-1){total+=count;if(total>128L*1024*1024)throw new IOException("Selected file exceeds 128 MB");target.write(buffer,0,count);}
                }
                complete(output.getAbsolutePath());
            } catch(Exception e) { if(output!=null)output.delete();complete("ERROR:Could not copy selected file: "+e.getMessage()); }
        },"AncestorWalk-document-copy").start();
    }
    private synchronized void complete(String value) {
        if(sent)return;sent=true;
        try {Class.forName("com.unity3d.player.UnityPlayer").getMethod("UnitySendMessage",String.class,String.class,String.class).invoke(null,callback,"OnFilePicked",value);}
        catch(Exception ignored) {}
        runOnUiThread(this::finish);
    }
}
